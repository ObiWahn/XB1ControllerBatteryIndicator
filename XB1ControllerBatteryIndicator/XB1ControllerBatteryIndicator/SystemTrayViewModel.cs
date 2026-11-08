using System.Linq;
using System.Threading;
using Windows.UI.Notifications;
using Windows.Data.Xml.Dom;
using System.Collections.Generic;
using System;
using System.Management;
using System.Collections.ObjectModel;
using System.IO;
using System.Diagnostics;
using System.Globalization;
using System.Media;
using XB1ControllerBatteryIndicator.ShellHelpers;
using XB1ControllerBatteryIndicator.Localization;
using XB1ControllerBatteryIndicator.Properties;
using System.Security.Principal;
using Microsoft.Win32;

namespace XB1ControllerBatteryIndicator
{
    public class SystemTrayViewModel : Caliburn.Micro.Screen
    {
        private string _activeIcon;
        private string _tooltipText;
        private const string APP_ID = "NiyaShy.XB1ControllerBatteryIndicator";
        private const int DisplayIntervalMs = 5000;
        private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(20);
        private static readonly TimeSpan FastCheckInterval = TimeSpan.FromSeconds(5);
        private readonly AutoResetEvent _checkNow = new AutoResetEvent(false);
        private readonly Controller[] _controllers =
        {
            new Controller(UserIndex.One), new Controller(UserIndex.Two), new Controller(UserIndex.Three),
            new Controller(UserIndex.Four)
        };
        //result of the last check, same order as _controllers, null if not connected
        private readonly BatteryInformation?[] _batteryInfo = new BatteryInformation?[4];
        private bool[] toast_shown = new bool[5];
        private bool[] lowBatterySoundPlayed = new bool[5];
        private Dictionary<string, int> numdict = new Dictionary<string, int>();
        private const string ThemeRegKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
        private const string ThemeRegValueName = "SystemUsesLightTheme";

        private SoundPlayer _soundPlayer;

        public SystemTrayViewModel()
        {
            GetAvailableLanguages();
            TranslationManager.CurrentLanguageChangedEvent += (sender, args) => GetAvailableLanguages();
            UpdateNotificationSound();

            ActiveIcon = $"Resources/battery_unknown{LightTheme()}.ico";
            numdict["One"] = 1;
            numdict["Two"] = 2;
            numdict["Three"] = 3;
            numdict["Four"] = 4;
            TryCreateShortcut();
            Thread th = new Thread(RefreshControllerState);
            th.IsBackground = true;
            th.Start();
        }

        public string ActiveIcon
        {
            get { return _activeIcon; }
            set { Set(ref _activeIcon, value); }
        }

        public string TooltipText
        {
            get { return _tooltipText; }
            set { Set(ref _tooltipText, value); }
        }

        public ObservableCollection<CultureInfo> AvailableLanguages { get; } = new ObservableCollection<CultureInfo>();

        //wakes up every few seconds to rotate the displayed controller, checks the controllers only when due
        private void RefreshControllerState()
        {
            var nextCheck = DateTime.MinValue;
            var displayIndex = -1;

            while (true)
            {
                try
                {
                    var now = DateTime.UtcNow;
                    if (now >= nextCheck)
                    {
                        nextCheck = now + (Settings.Default.FastPolling ? FastCheckInterval : CheckInterval);
                        CheckControllers();
                    }
                    displayIndex = NextConnectedIndex(displayIndex);
                    ShowController(displayIndex);
                    PlayLoopedWarningSound();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(ex);
                }
                //always wait, also after an error, so a persistent failure cannot spin the loop
                if (_checkNow.WaitOne(DisplayIntervalMs))
                {
                    nextCheck = DateTime.MinValue;
                }
            }
        }

        //request a check right away instead of waiting for the next interval
        public void CheckNow()
        {
            _checkNow.Set();
        }

        //the setting stores the highest BatteryLevel that triggers the warning
        public bool IsWarningThresholdEmpty => Settings.Default.LowBatteryWarningThreshold == (int)BatteryLevel.Empty;
        public bool IsWarningThresholdLow => Settings.Default.LowBatteryWarningThreshold == (int)BatteryLevel.Low;

        public void SetWarningThreshold(int batteryLevel)
        {
            Settings.Default.LowBatteryWarningThreshold = batteryLevel;
            Settings.Default.Save();
            NotifyOfPropertyChange(nameof(IsWarningThresholdEmpty));
            NotifyOfPropertyChange(nameof(IsWarningThresholdLow));
            CheckNow();
        }

        //the menu checkbox already changed the setting through its binding
        public void FastPollingChanged()
        {
            Settings.Default.Save();
            CheckNow();
        }

        private void CheckControllers()
        {
            for (int i = 0; i < _controllers.Length; i++)
            {
                var controller = _controllers[i];
                BatteryInformation? batteryInfo = null;
                if (controller.IsConnected)
                {
                    try
                    {
                        batteryInfo = controller.GetBatteryInformation(BatteryDeviceType.Gamepad);
                    }
                    catch (System.ComponentModel.Win32Exception ex)
                    {
                        //disconnected between the two calls
                        Debug.WriteLine(ex);
                    }
                }
                _batteryInfo[i] = batteryInfo;
                UpdateWarning(controller.UserIndex, batteryInfo);
            }
        }

        //show the toast and play the sound once when a controller reaches the warning level,
        //reset both when it is above it again, wired or disconnected
        private void UpdateWarning(UserIndex controllerIndex, BatteryInformation? batteryInfo)
        {
            int controllerId = numdict[$"{controllerIndex}"];
            if (!IsBatteryWarning(batteryInfo))
            {
                lowBatterySoundPlayed[controllerId] = false;
                if (toast_shown[controllerId])
                {
                    toast_shown[controllerId] = false;
                    ToastNotificationManager.History.Remove($"Controller{controllerIndex}", "ControllerToast", APP_ID);
                }
                return;
            }
            //check if toast (notification) for this controller was already triggered
            if (!toast_shown[controllerId])
            {
                toast_shown[controllerId] = true;
                ShowToast(controllerIndex, batteryInfo.Value.BatteryLevel);
            }
            //with "repeat on loop" the sound is played by PlayLoopedWarningSound instead
            if (Settings.Default.LowBatteryWarningSound_Enabled && !Settings.Default.LowBatteryWarningSound_Loop_Enabled &&
                !lowBatterySoundPlayed[controllerId])
            {
                lowBatterySoundPlayed[controllerId] = true;
                PlayWarningSound();
            }
        }

        private static bool IsBatteryWarning(BatteryInformation? batteryInfo)
        {
            if (batteryInfo == null)
            {
                return false;
            }
            //wired controllers and controllers still waiting for data report no meaningful level
            var batteryType = batteryInfo.Value.BatteryType;
            bool onBattery = batteryType == BatteryType.Alkaline || batteryType == BatteryType.Nimh;
            return onBattery && (int)batteryInfo.Value.BatteryLevel <= Settings.Default.LowBatteryWarningThreshold;
        }

        private void PlayLoopedWarningSound()
        {
            if (Settings.Default.LowBatteryWarningSound_Enabled && Settings.Default.LowBatteryWarningSound_Loop_Enabled &&
                _batteryInfo.Any(IsBatteryWarning))
            {
                PlayWarningSound();
            }
        }

        private void PlayWarningSound()
        {
            //Necessary to avoid crashing if the .wav file is missing
            try
            {
                _soundPlayer?.Play();
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
            }
        }

        //index of the next connected controller after current, -1 if none is connected
        private int NextConnectedIndex(int current)
        {
            for (int offset = 1; offset <= _batteryInfo.Length; offset++)
            {
                int candidate = (current + offset) % _batteryInfo.Length;
                if (_batteryInfo[candidate] != null)
                {
                    return candidate;
                }
            }
            return -1;
        }

        private void ShowController(int index)
        {
            if (index < 0)
            {
                TooltipText = Strings.ToolTip_NoController;
                ActiveIcon = $"Resources/battery_unknown{LightTheme()}.ico";
                return;
            }

            var controllerIndex = _controllers[index].UserIndex;
            var batteryInfo = _batteryInfo[index].Value;
            var controllerIndexCaption = GetControllerIndexCaption(controllerIndex);
            //wired
            if (batteryInfo.BatteryType == BatteryType.Wired)
            {
                TooltipText = string.Format(Strings.ToolTip_Wired, controllerIndexCaption);
                ActiveIcon = $"Resources/battery_wired_{controllerIndex.ToString().ToLower() + LightTheme()}.ico";
            }
            //"disconnected", a controller that was detected but hasn't sent battery data yet has this state
            else if (batteryInfo.BatteryType == BatteryType.Disconnected)
            {
                TooltipText = string.Format(Strings.ToolTip_WaitingForData, controllerIndexCaption);
                ActiveIcon = $"Resources/battery_disconnected_{controllerIndex.ToString().ToLower() + LightTheme()}.ico";
            }
            //this state should never happen
            else if (batteryInfo.BatteryType == BatteryType.Unknown)
            {
                TooltipText = string.Format(Strings.ToolTip_Unknown, controllerIndexCaption);
                ActiveIcon = $"Resources/battery_disconnected_{controllerIndex.ToString().ToLower() + LightTheme()}.ico";
            }
            //a battery level was detected
            else
            {
                var batteryLevelCaption = GetBatteryLevelCaption(batteryInfo.BatteryLevel);
                TooltipText = string.Format(Strings.ToolTip_Wireless, controllerIndexCaption, batteryLevelCaption);
                ActiveIcon = $"Resources/battery_{batteryInfo.BatteryLevel.ToString().ToLower()}_{controllerIndex.ToString().ToLower() + LightTheme()}.ico";
            }
        }

        //try to create a start menu shortcut (required for sending toasts)
        private bool TryCreateShortcut()
        {
            String shortcutPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) + "\\Microsoft\\Windows\\Start Menu\\Programs\\XB1ControllerBatteryIndicator.lnk";
            if (!File.Exists(shortcutPath))
            {
                InstallShortcut(shortcutPath);
                return true;
            }
            return false;
        }
        //create the shortcut
        private void InstallShortcut(String shortcutPath)
        {
            // Find the path to the current executable 
            String exePath = Process.GetCurrentProcess().MainModule.FileName;
            IShellLinkW newShortcut = (IShellLinkW)new CShellLink();

            // Create a shortcut to the exe 
            ErrorHelper.VerifySucceeded(newShortcut.SetPath(exePath));
            ErrorHelper.VerifySucceeded(newShortcut.SetArguments(""));

            // Open the shortcut property store, set the AppUserModelId property 
            IPropertyStore newShortcutProperties = (IPropertyStore)newShortcut;

            using (PropVariant appId = new PropVariant(APP_ID))
            {
                ErrorHelper.VerifySucceeded(newShortcutProperties.SetValue(PropertyKeys.AppUserModelId, appId));
                ErrorHelper.VerifySucceeded(newShortcutProperties.Commit());
            }

            // Commit the shortcut to disk 
            IPersistFile newShortcutSave = (IPersistFile)newShortcut;

            ErrorHelper.VerifySucceeded(newShortcutSave.Save(shortcutPath, true));
        }
        //send a toast
        private void ShowToast(UserIndex controllerIndex, BatteryLevel batteryLevel)
        {
            int controllerId = numdict[$"{controllerIndex}"];
            var controllerIndexCaption = GetControllerIndexCaption(controllerIndex);
            var toastText = batteryLevel == BatteryLevel.Empty ? Strings.Toast_Text : Strings.Toast_TextLow;
            string argsDismiss = $"dismissed";
            string argsLaunch = $"{controllerId}";
            //how the content gets arranged
            string toastVisual =
                $@"<visual>
                        <binding template='ToastGeneric'>
                            <text>{string.Format(Strings.Toast_Title, controllerIndexCaption)}</text>
                            <text>{string.Format(toastText, controllerIndexCaption)}</text>
                            <text>{Strings.Toast_Text2}</text>
                        </binding>
                    </visual>";
            //Button on the toast
            string toastActions =
                $@"<actions>
                        <action content='{Strings.Toast_Dismiss}' arguments='{argsDismiss}'/>
                   </actions>";
            //combine content and button
            string toastXmlString =
                $@"<toast scenario='reminder' launch='{argsLaunch}'>
                        {toastVisual}
                        {toastActions}
                   </toast>";

            XmlDocument toastXml = new XmlDocument();
            toastXml.LoadXml(toastXmlString);
            //create the toast
            var toast = new ToastNotification(toastXml);
            toast.Activated += ToastActivated;
            toast.Dismissed += ToastDismissed;
            toast.Tag = $"Controller{controllerIndex}";
            toast.Group = "ControllerToast";
            //..and send it
            ToastNotificationManager.CreateToastNotifier(APP_ID).Show(toast);

        }
        //react to click on toast or button
        private void ToastActivated(ToastNotification sender, object e)
        {
            var toastArgs = e as ToastActivatedEventArgs;
            int controllerId = 0;
            //if the return value contains a controller ID
            if (Int32.TryParse(toastArgs.Arguments, out controllerId))
            {
                //reset the toast warning (it will trigger again if battery level is still empty)
                toast_shown[controllerId] = false;
            }
            //otherwise, do nothing
        }
        private void ToastDismissed(ToastNotification sender, object e)
        {
            //do nothing
        }

        public void ExitApplication()
        {
            System.Windows.Application.Current.Shutdown();
        }

        private string GetBatteryLevelCaption(BatteryLevel batteryLevel)
        {
            switch (batteryLevel)
            {
                case BatteryLevel.Empty:
                    return Strings.BatteryLevel_Empty;
                case BatteryLevel.Low:
                    return Strings.BatteryLevel_Low;
                case BatteryLevel.Medium:
                    return Strings.BatteryLevel_Medium;
                case BatteryLevel.Full:
                    return Strings.BatteryLevel_Full;
                default:
                    throw new ArgumentOutOfRangeException(nameof(batteryLevel), batteryLevel, null);
            }
        }

        private string GetControllerIndexCaption(UserIndex index)
        {
            switch (index)
            {
                case UserIndex.One:
                    return Strings.ControllerIndex_One;
                case UserIndex.Two:
                    return Strings.ControllerIndex_Two;
                case UserIndex.Three:
                    return Strings.ControllerIndex_Three;
                case UserIndex.Four:
                    return Strings.ControllerIndex_Four;
                default:
                    throw new ArgumentOutOfRangeException(nameof(index), index, null);
            }
        }

        private void GetAvailableLanguages()
        {
            AvailableLanguages.Clear();
            foreach (var language in TranslationManager.AvailableLanguages)
            {
                AvailableLanguages.Add(language);
            }
        }

        public void UpdateNotificationSound()
        {
            _soundPlayer = File.Exists(Settings.Default.wavFile) ? new SoundPlayer(Settings.Default.wavFile) : null;
        }
        public void WatchTheme()
        {
            var currentUser = WindowsIdentity.GetCurrent();
            string query = string.Format(
                CultureInfo.InvariantCulture,
                @"SELECT * FROM RegistryValueChangeEvent WHERE Hive = 'HKEY_USERS' AND KeyPath = '{0}\\{1}' AND ValueName = '{2}'",
                currentUser.User.Value,
                ThemeRegKeyPath.Replace(@"\", @"\\"),
                ThemeRegValueName);

            try
            {
                var watcher = new ManagementEventWatcher(query);
                watcher.EventArrived += (sender, args) =>
                {
                    LightTheme();
                    
                };

                // Start listening for events
                watcher.Start();
            }
            catch (Exception)
            {
                // This can fail on Windows 7
            }

            LightTheme();
        }

        private string LightTheme()
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(ThemeRegKeyPath))
            {
                object registryValueObject = key?.GetValue(ThemeRegValueName);
                if (registryValueObject == null)
                {
                    return "";
                }

                int registryValue = (int)registryValueObject;

                return registryValue > 0 ? "-black" : "";
            }
        }
    }
}
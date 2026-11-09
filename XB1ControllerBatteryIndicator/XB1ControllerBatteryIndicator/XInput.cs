using System.ComponentModel;
using System.Runtime.InteropServices;

namespace XB1ControllerBatteryIndicator
{
    //the subset of XInput used by this app, see xinput.h
    //enum member names are used for icon file names and lookups, keep them unchanged

    public enum UserIndex : byte
    {
        One = 0,
        Two = 1,
        Three = 2,
        Four = 3,
    }

    public enum BatteryDeviceType : byte
    {
        Gamepad = 0,
        Headset = 1,
    }

    public enum BatteryType : byte
    {
        Disconnected = 0,
        Wired = 1,
        Alkaline = 2,
        Nimh = 3,
        Unknown = 0xFF,
    }

    public enum BatteryLevel : byte
    {
        Empty = 0,
        Low = 1,
        Medium = 2,
        Full = 3,
    }

    //XINPUT_BATTERY_INFORMATION
    [StructLayout(LayoutKind.Sequential)]
    public struct BatteryInformation
    {
        public BatteryType BatteryType;
        public BatteryLevel BatteryLevel;
    }

    public class Controller
    {
        private const uint ERROR_SUCCESS = 0;

        public Controller(UserIndex userIndex)
        {
            UserIndex = userIndex;
        }

        public UserIndex UserIndex { get; }

        public bool IsConnected => XInputGetState((uint)UserIndex, out _) == ERROR_SUCCESS;

        public BatteryInformation GetBatteryInformation(BatteryDeviceType deviceType)
        {
            uint result = XInputGetBatteryInformation((uint)UserIndex, deviceType, out BatteryInformation batteryInformation);
            if (result != ERROR_SUCCESS)
            {
                throw new Win32Exception((int)result);
            }
            return batteryInformation;
        }

        //XINPUT_STATE, the content is not used, only whether the call succeeds
        [StructLayout(LayoutKind.Sequential)]
        private struct State
        {
            public uint PacketNumber;
            public ushort Buttons;
            public byte LeftTrigger;
            public byte RightTrigger;
            public short ThumbLX;
            public short ThumbLY;
            public short ThumbRX;
            public short ThumbRY;
        }

        //load from System32 only, so a xinput1_4.dll next to the exe (e.g. in Downloads) is never picked up
        [DllImport("xinput1_4.dll"), DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern uint XInputGetState(uint dwUserIndex, out State pState);

        [DllImport("xinput1_4.dll"), DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern uint XInputGetBatteryInformation(uint dwUserIndex, BatteryDeviceType devType, out BatteryInformation pBatteryInformation);
    }
}

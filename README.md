# XBox One Controller Battery Indicator

> ## About this fork
> This fork changes the original project as follows:
> - **Auto update removed.** The update check downloaded version info over plain HTTP from a
>   third-party server and opened whatever link it contained. The feature and its menu entry are
>   gone; check this repository's Releases page for new versions instead.
> - **Requires the .NET 10 Desktop Runtime.** The app now targets .NET 10 instead of .NET Framework
>   4.5.2. Get the runtime from https://dotnet.microsoft.com/download/dotnet/10.0 if it is not
>   installed yet.
> - **Autostart path quoted.** The registry autostart entry now quotes the program path, so paths
>   containing spaces cannot be misinterpreted.
> - **Automated builds.** GitHub Actions builds the app on every push; tagged versions are published
>   as Releases with a ready-to-run `.exe`.
> - **Fewer third-party dependencies.** The unmaintained `SharpDX.XInput` (archived since 2019) and
>   the community repackage `Microsoft-WindowsAPICodePack-Shell` were replaced by small amounts of
>   own interop code that call `xinput1_4.dll` and the Windows property system directly.
> - **Less frequent controller checks.** Controllers are checked every 20 minutes instead of every few
>   seconds. Enable "Fast check" in the tray menu to check every 5 seconds, or left-click the tray
>   icon to check right away, e.g. after switching a controller on.
> - **New tray menu options:** warn at "Low" instead of only at "Empty" battery level, override the
>   taskbar theme used for the icon colour, and hide the icon while no controller is connected.
> - **Windows warning sound.** The low battery sound is the "Exclamation" sound of the Windows sound
>   scheme; picking a custom WAV file is no longer supported.
> - **Bug fixes in the polling loop.** Warnings are tracked per controller (all of them, not only the
>   one shown), the warning sound plays again each time a battery reaches the warning level, and an
>   error can no longer make the loop spin at full CPU load.

A tray application that shows a battery indicator for an Xbox-ish controller and gives a notification when the battery level drops to (almost) empty. 

It was originally written for the XBox One controller since Microsoft dropped all visual hints for low battery, but it should work with any gamepad that can be addressed via XInput (which should be all controllers that work in XBox-controller-enabled games).

**Download link for a "ready to run" version at the bottom of this page(or under "Releases" in the side bar)!**  
(The green download button at the top is for the source code package)

When more than one controller is present, the tray icon will cycle through the status display every 5 seconds.

![Tray icon](https://i.imgur.com/rxWAsu8.gif "Tray icon cycling through multiple controllers")

When a controller reaches low battery level, a notification is displayed.  

![Imgur](https://i.imgur.com/LPUBWtl.png "Toast message with low battery warning")


Controllers that will work with this program include:

* XBox 360
* XBox One (model 1537/1697)
* XB One Elite (model 1698)
* XB One S (model 1708)
* XB One Elite 2 (model 1797)
* XB Series X/S (model 1914)

Note that these controllers will **only work when you connect them via a dongle that uses Microsofts proprietary protocol**, though is doesn't seem to matter if it's an official or 3rd party version.
***Bluetooth connections will NOT work properly, see below!***

Currently known issues/limitations:
* Controllers connected via Bluetooth won't report any or a completely wrong battery level. This seems to be an issue with Microsofts BT implementation and I can't do anything about it. See Issue [#49](https://github.com/NiyaShy/XB1ControllerBatteryIndicator/issues/49) for details.
* initial recognition of a newly connected controller can take a while. It will be displayed as "waiting for battery level data" at first but should switch to battery level after ~10 seconds and a button press. (This might be a limitation of the XInput API.) Without "Fast check", the battery level only shows up at the next check; left-click the tray icon to check right away.

**[You can download the latest version here](../../releases).**  
Some additional details about how it works and what it shows can be found on the [wiki page](https://github.com/NiyaShy/XB1ControllerBatteryIndicator/wiki).  
Found a bug or wanna give feedback? Open an issue here in github or leave a comment over on [reddit](https://www.reddit.com/r/xb1cbi).
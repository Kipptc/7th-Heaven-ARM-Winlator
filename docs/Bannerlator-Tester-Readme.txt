7th Heaven Bannerlator test build (Steam 2013 FF7)

This v38 build includes the Wine x86 .NET loader workaround that passed an
extended Proton 11 ARM64 playthrough with mods and a game save. It leaves
stock FFNx in place.

Extract the complete folder into a Bannerlator Proton 11 ARM64 container.
Run 7th Heaven.exe and select the installed ff7_en.exe in General Settings.
If FFNx is missing, the first Play downloads and installs the stock FFNx release;
wait for that download to finish, then press Play again. This package uses the
prebuilt stock FFNx driver and does not replace it.

After pressing Play in 7th Heaven, launch ff7_en.exe through Bannerlator's FF7
game shortcut. This retains Bannerlator's Steam session. After the game closes,
opening 7th Heaven exports the previous FFNx.log from the FF7 game folder to
D:\Bannerlator\FINAL FANTASY VII (2013)\FFNx.log when that Downloads path is
available. Pressing Play also checks for a newer game log. The game loader
writes AppLoader.log in that same Downloads folder when it is available.

If a tester later stages modified FFNx files in an FFNx folder beside 7th
Heaven.exe, Play copies them into the game folder, backs up originals under
.7h-ffnx-original, and preserves the staged build during FFNx update checks.

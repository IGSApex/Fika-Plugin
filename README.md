# IGS Gaming Community — Fika Client Plugin

Community fork of [Project Fika's client plugin](https://github.com/project-fika/Fika-Plugin) for SPT 4.1.x, with custom multiplayer squad functionality and menu UI. The current `FikaPlugin.FikaVersion` constant is **2.4.6**.

## Community changes

### Online Players and squads

The main-menu **Online Players** list shows connected players and presence. Squad controls allow **Create Squad / Leave Squad** and **Manage Squad**. Squad management displays the current roster, leader, membership count (up to five), and the squad identifier; the client also handles invitations and squad notifications.

Key implementation files:

- `Fika.Core/UI/Custom/MainMenuUIScript.cs` — online-player panel and squad controls.
- `Fika.Core/Networking/Websocket/FikaSquadNotifications.cs` — squad state and incoming notifications.
- `Fika.Core/UI/Custom/MainMenuUI.cs` — original UI component references.

The squad footer is currently implemented by **cloning the refresh button's parent UI object** from the bundled Online Players prefab, removing cloned children and adding the squad buttons. This is intended to match the original header styling, but **pixel-perfect alignment has not been verified in-game**. Check the prefab hierarchy if the footer's texture or position differs. The squad roster has its own members header and small squad-ID footer.

### Fika 2.4.6 update

- `Fika.Core/FikaPlugin.cs`: `FikaVersion = "2.4.6"`.
- `Fika.Core/Main/Players/FikaPlayer.cs`: corpse health synchronization validates killer and weapon profile IDs before converting them to nullable `MongoID` values.

These changes preserve the community squad functionality. Keep the Fika client and server builds compatible.

## Build

Requirements: .NET 10 SDK, Windows 10/11, and a compatible SPT/EFT installation.

Populate the project's `References/` folder with the required assemblies from `EscapeFromTarkov_Data/Managed/` and the appropriate SPT Modules `hollowed.dll`, following the existing project references. Then:

```powershell
git clone https://github.com/IGSApex/Fika-Plugin.git
cd Fika-Plugin
dotnet restore
dotnet build -c Release
```

Deploy the built `Fika.Core.dll` and required assets into the matching client installation. Do not distribute reference assemblies copied from the game. A successful build does not guarantee correct Unity UI layout; test the menu and squad actions in-game.

## Related repositories

- [IGSApex/Fika-Server-CSharp](https://github.com/IGSApex/Fika-Server-CSharp) — authoritative squad membership and invitations.
- [IGSApex/launcher](https://github.com/IGSApex/launcher) — launcher and mod synchronization.
- [IGSApex/server-csharp](https://github.com/IGSApex/server-csharp) — SPT game server and launcher API.

This fork builds on Project Fika. See `LICENSE.md`, `Licenses/` and upstream credits for licensing and attribution.

# Desktop frontend coverage

Windows uses WinUI 3. Linux and macOS use Avalonia. Both frontends use Core for installation, authentication, resource providers, imports, exports, backups and launcher tools.

| Area | Avalonia implementation | Automated checks |
| --- | --- | --- |
| Navigation | Shared menu catalog; WinUI order, dimensions, selection indicators, collapse timing, task badge and runtime-log entry | Menu routes, interrupted animations, scaling, language changes |
| Home | Account/game cards, six account actions, optional performance/note/news cards, saved sizes/order, editor and card drawer | Catalog insertion, saved order, responsive layout, account action dimensions |
| Instances | Directory selector, loader filters/default sort, pinned favorites, shared `tile` setting, list/square cards, card selection and context actions | Page construction, content routes and Core directory/deletion tests |
| Installation | Release/snapshot/April Fools categories, untruncated virtual version list, separate install-options step and bounded loader lookup | Category separation, catalog size and step transition |
| Content | Common instance tabs, conditional schematic tab, virtualized installed content, metadata search/loader filters, schematic search and shared metadata/icon cache | Real fixture JAR metadata/icon, shared-cache deduplication and disk reuse |
| Community | Modrinth/CurseForge search, filters, manual pagination, project details and installation | Explicit search, request coalescing and Core provider/install tests |
| Blueprints/maps | Existing CMS, Schematicraft, MCSchematic and CurseForge map services | Core cache/rate-limit/parser/archive tests and page construction |
| Accounts | Microsoft, offline and authlib-injector flows; shared eligibility checks, cached account avatars and device-code copy | Unverified account selection, removal normalization, cached-avatar rendering |
| Settings | WinUI section order, immediate general persistence, explicit proxy apply/test, live theme brushes/native control palette and shared JSON locales | Saved memory/download settings and seven language packs |
| Data/tools | Mod updates, world backups, pack export, Java (including PATH selection), frpc, Terracotta, MinecraftPFP, logs and system status | Page construction and existing Core regression suites |
| Distribution | Existing Linux tar/deb/rpm/AppImage and macOS build targets | Cross-publish/package checks; Linux CI also runs the UI suite |

`CMCLauncher.Core/Services/LauncherNavigation.cs` is the navigation source for both frontends. `ContentInspectionCache` and `MinecraftSkins` live in Core; neither depends on a UI framework. Both frontends retain the same on-disk content metadata and skin cache locations. Image decoding and control lifetime remain in each frontend.

## Verification boundary

The Avalonia UI suite uses an isolated store and rejects unplanned HTTP requests. Development runs through a Windows test host verify Avalonia controls and state transitions, not Linux/macOS desktop integration. A successful cross-publish does not verify game execution, native drag gestures, file dialogs, Secret Service/Keychain, OS window management or the platform updater. Those require the respective host. Native typography and window decoration are supplied by the OS.

Run the target-platform suite with:

```sh
CMCLauncher.Avalonia --verify-ui /path/to/isolated-output
```

Check `passed.txt`, screenshots and absence of `failure.txt`. Do not run verification with a normal user data directory.

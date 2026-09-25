# KomachiJapaneseFix

Source code and Japanese localization for the unofficial [The Ferryman / Komachi Onozuka Japanese fix](https://steamcommunity.com/sharedfiles/filedetails/?id=3806241141) for Slay the Spire 2. The source and linked Workshop item are version 0.2.20. Both target the original Komachi mod version 0.2.2 on the game's `public-beta` branch. This release adds per-enemy distance-damage previews for all-enemy attacks, leaves release-card gold glow to the original mod, and keeps card-inspection arrows in the displayed order rather than exposing the draw pile's real order.

This is an add-on, not a copy of the original mod. The original mod, Slay the Spire 2, BaseLib, and MinionLib are required at runtime and are **not** included here. Install the playable version through the linked Steam Workshop item.

## For the original mod author

The C# patches are in `src/KomachiJapaneseFix/*.cs`; the Japanese translation tables are in `src/KomachiJapaneseFix/Localization/jpn/`. Translation strings and behavior fixes are separate so they can be reviewed and incorporated independently. Please compare patches against the current game and dependency APIs before integration.

Six PNG illustrations used by the Workshop build are **not** in this repository because their redistribution status has not been confirmed. This repository is therefore a source-review snapshot, not a byte-for-byte or visually complete replacement for the Workshop package. When those embedded resources are missing, the visual patches fall back to the original card portraits.

## Local build

Install .NET 9 SDK, Slay the Spire 2, and subscribe to BaseLib, MinionLib, and the original Komachi mod. Set `STS2_GAME_DIR` to the game folder and `STS2_WORKSHOP_DIR` to Steam's `steamapps/workshop/content/2868840` folder, then build the project:

```powershell
$env:STS2_GAME_DIR = 'PATH_TO_SLAY_THE_SPIRE_2'
$env:STS2_WORKSHOP_DIR = 'PATH_TO_STEAMAPPS_WORKSHOP_CONTENT_2868840'
dotnet build src/KomachiJapaneseFix/KomachiJapaneseFix.csproj -c Release
```

The project references installed game and Workshop DLLs; it does not download or redistribute them. This build omits the unpublished PNG resources and is intended for code review. Use the Steam Workshop package for gameplay.

## Rights and credits

The original mod is by Valon. This add-on and its Japanese localization are by gizinca. The original mod and all third-party material remain the property of their respective owners. No general reuse license is granted merely by this public repository. The original mod author has separately been given permission to incorporate the add-on changes and Japanese translation into the main mod.

---

## 日本語

本リポジトリは、Slay the Spire 2 の小町MOD向け追加修正版 v0.2.20 のC#ソースと日本語訳を閲覧するためのものです。全体攻撃の各対象にも間合い別ダメージを表示し、解放カードの金色発光は元MODの判定に統一しました。山札選択中のカード詳細は画面の表示順に従い、山札の実際の順番を明かしません。元MOD・ゲーム・依存MODのDLLや、再配布可否を確認できていない画像は含めていません。プレイには上記Workshop版を使用してください。公開されていること自体を、第三者素材の再利用許諾とは扱わないでください。

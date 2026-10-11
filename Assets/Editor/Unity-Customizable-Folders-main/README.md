# Unity Customizable Folders

Color and badge Unity Project window folders with simple, portable rules. Drop this folder into any project and style folders by name or exact path — no setup scripts required.

## Features

- **Folder tinting**: Apply a custom color to the default folder icon in the Project window.
- **Icon badges**: Optionally overlay a small texture on top of the folder icon.
- **Two match modes**: Match by folder name (e.g. every `Scripts` folder) or by full asset path (e.g. `Assets/Art/Characters`).
- **Rule priority**: Drag rules to reorder them; the first matching enabled rule wins.
- **Portable**: Copy the entire `Custom Folders` folder anywhere under `Assets/`; settings and paths resolve automatically.

## Installation

1. Copy this repository (or the `Custom Folders` folder) into your Unity project under `Assets/`, for example:

   ```
   Assets/Editor/Custom Folders/
   ```

2. Unity will compile the editor assembly automatically. No additional packages are required.

3. Open the settings asset via **Tools → Custom Folders → Open Settings**, or select `CustomFolderSettings.asset` in the Project window.

## Usage

### Opening settings

- **Menu:** Tools → Custom Folders → Open Settings
- **Asset:** Select `CustomFolderSettings.asset` (created next to the scripts on first open)

### Adding a rule

1. Open the settings asset in the Inspector.
2. Click **Add Selected Folder** with a folder selected in the Project window to create a full-path rule, or use the list's **+** button to add a rule manually.
3. Set a **Color** and optionally assign an **Icon Badge** texture.
4. Drag rules to change priority.

### Match modes

| Mode | Matches | Example |
|------|---------|---------|
| **Folder Name** | Any folder with that name, anywhere in the project | `Scripts` styles every folder named Scripts |
| **Full Path** | One exact folder path | `Assets/Art/UI` styles only that folder |

**Tip:** Use **Folder Name** when sharing settings across projects. Use **Full Path** when you need to style a specific folder.

### Example rules

The included `CustomFolderSettings.asset` comes with sample rules for common folder names such as `Scripts`, `Scriptable Objects`, and `Prefabs`. Edit or remove them to suit your project.

## Project structure

```
Custom Folders/
├── CustomFolderCustomization.cs   # Project window drawing & settings bootstrap
├── CustomFolderSettings.cs        # ScriptableObject holding style rules
├── CustomFolderSettingsEditor.cs  # Custom Inspector UI
├── CustomFolderSettings.asset     # Your rules (editable in the Inspector)
├── FolderStyleRule.cs             # Rule data & matching logic
├── CustomFolders.Editor.asmdef    # Editor-only assembly definition
└── Icons/                         # Optional badge textures
```

## Requirements

- Unity 2020.3 or newer (uses standard Editor APIs)
- Editor only — no runtime impact on builds

## How it works

On load, `CustomFolderCustomization` hooks into `EditorApplication.projectWindowItemOnGUI`. For each folder in the Project window, it looks up the first enabled rule that matches and redraws the folder icon with the configured tint and optional badge overlay.

Settings are stored in `CustomFolderSettings.asset` beside the scripts. If the asset is missing, one is created automatically with a sample `Scripts` rule when you open settings from the menu.

## License

See the repository for license information.

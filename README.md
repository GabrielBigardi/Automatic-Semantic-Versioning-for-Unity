# Automatic Semantic Versioning for Unity

An automated, Git-driven Semantic Versioning build processor for Unity. 

This script hooks into Unity's pre-build process, reads your Git commit history, automatically calculates the next version number based on Conventional Commits, and injects it directly into your `PlayerSettings.bundleVersion`.

## 🎮 Why 4 Digits? (The Game Dev Dilemma)

Standard Semantic Versioning (SemVer) uses a strict 3-digit format: `MAJOR.MINOR.PATCH` (e.g., `1.0.0`). 

While perfect for web APIs and software libraries, standard SemVer clashes with player expectations in game development:
*   In standard SemVer, version `0.x.x` means "initial development" and any breaking change bumps it to `1.0.0`, `2.0.0`, etc.
*   **To players**, `v0.x` means "Demo or Early Access" and `v1.0` strictly means "Full Game Launch." 

If a game strictly follows 3-digit SemVer, overhauling the save system during Early Access would force the version to jump to `1.0.0`, falsely signaling to players that the game has fully launched.

### The Solution: `STAGE.MAJOR.MINOR.PATCH`
This script implements a 4-digit system specifically tailored for game development. 

1.  **Stage (Manual):** Represents the marketing/release state of the game (`0` = Early Access/Demo, `1` = 1.0 Launch, `2` = Massive Sequel Update). This number *only* changes when you explicitly tell it to.
2.  **Major (Breaking):** Bumps when you introduce a massive, breaking change (e.g., wiping save files, overhauling networking). 
3.  **Minor (Feature):** Bumps when you add new, backwards-compatible content or mechanics.
4.  **Patch (Fix):** Bumps when you fix bugs or typos.

---

## ✨ Features

*   **Zero Tag Dependency:** Unlike standard CI tools, this script does not rely on Git tags. It uses a lightweight `ProjectSettings/AutoSemVerState.json` file to track the hash of your last build, instantly skipping calculations if no new commits were made.
*   **Plug & Play:** Drop it into an existing project. If it's the first time running, it will automatically adopt your current Unity version, format it to 4 digits (e.g., `0.4` becomes `0.4.0.0`), and establish your baseline.
*   **Manual Stage Control via Git:** Trigger your "1.0 Launch" directly from your commit message without opening any configuration files.

---

## 🚀 Installation

1. Ensure **Git** is installed and added to your system's PATH (it must be accessible via your command line/terminal).
2. Create an `Editor` folder anywhere in your Unity project's `Assets` directory.
3. Place `AutoSemVerBuildProcessor.cs` inside the `Editor` folder.

That's it. The next time you click **File > Build Settings > Build**, the script will run automatically.

---

## 📖 How to Write Your Commits

The script parses your Git commit messages looking for specific prefixes to calculate the math. 

### 1. Patch Bumps (Fixes)
Use the `fix:` prefix.
```text
fix: resolve clipping issue on arena walls
```
> **Result:** `0.1.0.0` ➔ `0.1.0.1`

### 2. Minor Bumps (Features)
Use the `feat:` prefix.
```text
feat: add new double jump mechanic
```
> **Result:** `0.1.0.1` ➔ `0.1.1.0` (Patch resets to 0)

### 3. Major Bumps (Breaking Changes)
Add an exclamation point `!` before the colon, or write `BREAKING CHANGE:` in the body.
```text
feat!: completely rewrite the save file architecture
```
> **Result:** `0.1.1.0` ➔ `0.2.0.0` (Minor and Patch reset to 0)

### 4. Stage Bumps (Manual Release)
When you are ready to leave Early Access or trigger a massive marketing milestone, use the `release(X):` prefix, replacing X with your target stage.
```text
release(1): launch game out of early access
```
> **Result:** `0.2.0.0` ➔ `1.0.0.0` (All other numbers reset to 0)

*(Note: If you commit chores like `docs: update readme` or `style: format code`, the script recognizes them but will safely leave the version number unchanged).*

---

## 🛠️ Where is the data stored?

The script creates a tiny JSON file at `ProjectSettings/AutoSemVerState.json`. 
Because it is in the `ProjectSettings` folder, it won't clutter your Unity Assets window, but it **will** be tracked by Git. This ensures that if you work on a team, everyone shares the exact same version state and build hash baseline.

using System;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using Debug = UnityEngine.Debug;

[Serializable]
public class SemVerState
{
    public string LastBuildHash = "";
    public string CurrentVersion = "0.0.0.0";
}

public class AutoSemVerBuildProcessor : IPreprocessBuildWithReport
{
    public int callbackOrder => 0;
    
    private const string STATE_FILE_PATH = "ProjectSettings/AutoSemVerState.json";

    public void OnPreprocessBuild(BuildReport report)
    {
        Debug.Log("[AutoSemVer] Checking version state...");

        try
        {
            var currentHeadHash = RunGitCommand("rev-parse HEAD").Trim();
            if (string.IsNullOrWhiteSpace(currentHeadHash))
            {
                Debug.LogWarning("[AutoSemVer] Could not retrieve Git hash. Is this a Git repository?");
                return;
            }

            var state = LoadState();

            // 1. FIRST TIME SETUP: Establish the baseline and force W.X.Y.Z format
            if (string.IsNullOrEmpty(state.LastBuildHash))
            {
                TryParseVersion(PlayerSettings.bundleVersion, out var initStage, out var initMaj, out var initMin, out var initPat);
                var strictInitialVersion = $"{initStage}.{initMaj}.{initMin}.{initPat}";

                Debug.Log($"[AutoSemVer] First run detected. Formatting baseline to {strictInitialVersion}");
                
                PlayerSettings.bundleVersion = strictInitialVersion;
                state.CurrentVersion = strictInitialVersion;
                state.LastBuildHash = currentHeadHash;
                SaveState(state);
                return;
            }

            // 2. CONSECUTIVE BUILD CHECK: No new commits
            if (state.LastBuildHash == currentHeadHash)
            {
                Debug.Log($"[AutoSemVer] No new commits since last build. Version remains {state.CurrentVersion}");
                PlayerSettings.bundleVersion = state.CurrentVersion; 
                return;
            }

            // 3. NEW COMMITS DETECTED
            Debug.Log($"[AutoSemVer] New commits found. Calculating bump from {state.CurrentVersion}...");
            
            var commitsOutput = RunGitCommand($"log {state.LastBuildHash}..HEAD --format=\"%s%n%b\"");
            
            if (TryParseVersion(state.CurrentVersion, out var stage, out var major, out var minor, out var patch))
            {
                var bumped = CalculateNextVersion(commitsOutput, ref stage, ref major, ref minor, ref patch);

                if (bumped)
                {
                    var newVersion = $"{stage}.{major}.{minor}.{patch}";
                    
                    PlayerSettings.bundleVersion = newVersion;
                    state.CurrentVersion = newVersion;
                    state.LastBuildHash = currentHeadHash;
                    SaveState(state);
                    
                    Debug.Log($"[AutoSemVer] Successfully bumped version to {newVersion}");
                }
                else
                {
                    state.LastBuildHash = currentHeadHash;
                    SaveState(state);
                    Debug.Log($"[AutoSemVer] Commits analyzed, but no version bump required. Version remains {state.CurrentVersion}");
                }
            }
            else
            {
                Debug.LogError($"[AutoSemVer] Failed to parse previous version: {state.CurrentVersion}");
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[AutoSemVer] Failed during process: {e.Message}");
        }
    }

    private SemVerState LoadState()
    {
        if (File.Exists(STATE_FILE_PATH))
        {
            var json = File.ReadAllText(STATE_FILE_PATH);
            return JsonUtility.FromJson<SemVerState>(json) ?? new SemVerState();
        }
        return new SemVerState();
    }

    private void SaveState(SemVerState state)
    {
        var json = JsonUtility.ToJson(state, true);
        File.WriteAllText(STATE_FILE_PATH, json);
    }

    // UPDATED PARSER: Handles up to 4 numbers
    private bool TryParseVersion(string versionString, out int stage, out int major, out int minor, out int patch)
    {
        stage = major = minor = patch = 0;
        if (string.IsNullOrWhiteSpace(versionString)) return false;

        var cleanVersion = versionString.Trim().TrimStart('v', 'V');
        var parts = cleanVersion.Split('.');
        
        if (parts.Length > 0) int.TryParse(parts[0], out stage);
        if (parts.Length > 1) int.TryParse(parts[1], out major);
        if (parts.Length > 2) int.TryParse(parts[2], out minor);
        if (parts.Length > 3) int.TryParse(parts[3], out patch);
        
        return true; 
    }

    private bool CalculateNextVersion(string commitsText, ref int stage, ref int major, ref int minor, ref int patch)
    {
        if (string.IsNullOrWhiteSpace(commitsText)) return false;

        var targetStage = -1; // -1 means no manual stage bump requested
        var bumpMajor = false;
        var bumpMinor = false;
        var bumpPatch = false;

        var lines = commitsText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        var commitRegex = new Regex(@"^(build|chore|ci|docs|feat|fix|perf|refactor|revert|style|test|release)(\([^)]+\))?(!)?:\s.*", RegexOptions.IgnoreCase);

        foreach (var line in lines)
        {
            if (line.Contains("BREAKING CHANGE:"))
            {
                bumpMajor = true;
                // We do NOT break here anymore, because a 'release' commit might be further up the history
            }

            var match = commitRegex.Match(line);
            if (match.Success)
            {
                var type = match.Groups[1].Value.ToLower();
                var scope = match.Groups[2].Value.Trim('(', ')');
                var isBreaking = match.Groups[3].Success; 

                // 1. Check for manual Release/Stage bump
                if (type == "release" && int.TryParse(scope, out int releaseNum))
                {
                    if (releaseNum > targetStage) targetStage = releaseNum;
                    continue; 
                }

                // 2. Check for Breaking Changes (Bumps Major)
                if (isBreaking)
                {
                    bumpMajor = true;
                    continue; 
                }
                
                // 3. Check standard features and fixes
                if (type == "feat") bumpMinor = true;
                else if (type == "fix") bumpPatch = true;
            }
        }

        // Apply Priority Logic (Highest priority bump wins)
        if (targetStage >= 0 && targetStage > stage)
        {
            stage = targetStage;
            major = 0;
            minor = 0;
            patch = 0;
            Debug.Log($"[AutoSemVer] STAGE bump triggered. Transitioning to Release Stage {stage}!");
            return true;
        }
        else if (targetStage >= 0 && targetStage <= stage)
        {
            Debug.LogWarning($"[AutoSemVer] Ignored release({targetStage}) because current Stage is already {stage} or higher.");
        }

        if (bumpMajor)
        {
            major++;
            minor = 0;
            patch = 0;
            Debug.Log("[AutoSemVer] MAJOR bump triggered.");
            return true;
        }
        if (bumpMinor)
        {
            minor++;
            patch = 0;
            Debug.Log("[AutoSemVer] MINOR bump triggered.");
            return true;
        }
        if (bumpPatch)
        {
            patch++;
            Debug.Log("[AutoSemVer] PATCH bump triggered.");
            return true;
        }

        return false;
    }

    private string RunGitCommand(string arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = Application.dataPath 
        };

        using (var process = Process.Start(startInfo))
        {
            process.WaitForExit();
            if (process.ExitCode != 0) return string.Empty;
            return process.StandardOutput.ReadToEnd();
        }
    }
}
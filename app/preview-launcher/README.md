# Timer Branch Launcher

To produce a fresh download, open **Development builds (experimental and alpha)** in GitHub Actions, select **Run workflow**, and enable **Include the Timer Branch Launcher download**. Download **Timer-Branch-Launcher-Windows** from that run, extract the executable and open it.

Choose Alpha or Experimental, then click **Download and launch**. Use **Refresh branches** to populate the picker from GitHub, or type the exact name of a preview branch. The launcher looks for the latest successful Windows artifact that is still available. A feature branch needs a portable artifact from a manual development workflow run.

Downloads and branch refresh use GitHub CLI. Install it if needed, then use **GitHub sign-in** to authenticate. **Launch installed** and **Roll back and launch** work offline.

The default install folder is `%LOCALAPPDATA%\BAT Launcher`. To reuse your updater's builds and data, place the executable beside its `test-install` folder or select that folder's parent with **Choose folder**. Each branch has a separate data directory. Rollback switches executables and makes a data backup first. It leaves the database format as it is.

Build the self-contained Windows x64 executable with `dotnet publish app/preview-launcher/TimerBranchLauncher.csproj -c Release`. The executable embeds the updater worker and displays its progress in the window.

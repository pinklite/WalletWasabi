using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using WalletWasabi.Helpers;

namespace WalletWasabi.Fluent.Helpers
{
	public static class StartupHelper
	{
		private const string KeyPath = "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run";

		// Arguments to add Wasabi to macOS startup settings.
		private static readonly string AddCmd = $"osascript -e \' tell application \"System Events\" to make new login item at end with properties {{name:\"WasabiWallet\", path:\"/Applications/{Constants.AppName}\",hidden:false}} \'";

		// Arguments to delete Wasabi from macOS startup settings.
		private static readonly string DeleteCmd = "osascript -e \' tell application \"System Events\" to delete login item \"WasabiWallet\" \'";

		public static async Task ModifyStartupSettingAsync(bool runOnSystemStartup)
		{
			if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
			{
				string pathToExeFile = EnvironmentHelpers.GetExecutablePath();
				if (!File.Exists(pathToExeFile))
				{
					throw new InvalidOperationException($"Path {pathToExeFile} does not exist.");
				}
				StartOnWindowsStartup(runOnSystemStartup, pathToExeFile);
			}
			else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
			{
				throw new NotImplementedException();
			}
			else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
			{
				await StartOnMacStartUpAsync(runOnSystemStartup);
			}
		}

		private static void StartOnWindowsStartup(bool runOnSystemStartup, string pathToExeFile)
		{
			if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
			{
				throw new InvalidOperationException("Registry modification can only be done on Windows.");
			}

			using RegistryKey key = Registry.CurrentUser.OpenSubKey(KeyPath, writable: true) ?? throw new InvalidOperationException("Registry operation failed.");
			if (runOnSystemStartup)
			{
				key.SetValue(nameof(WalletWasabi), pathToExeFile);
			}
			else
			{
				key.DeleteValue(nameof(WalletWasabi), false);
			}
		}

		private static async Task StartOnMacStartUpAsync(bool runOnSystemStartup)
		{
			if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
			{
				throw new InvalidOperationException("Running osascript can only be done on macOS.");
			}

			if (runOnSystemStartup)
			{
				await EnvironmentHelpers.ShellExecAsync(AddCmd);
			}
			else
			{
				await EnvironmentHelpers.ShellExecAsync(DeleteCmd);
			}
		}
	}
}

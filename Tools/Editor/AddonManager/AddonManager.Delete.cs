/**
 * AddonManager.Delete
 * Author: Denarii Games
 * Version: 1.0-rc1
 *
 * Delete related functionality.
 */

using UnityEngine;
using UnityEditor;

namespace OpenMMORPG.AddonManager
{
    public partial class AddonManagerWindow
    {
		/// <summary>
		/// Callback from button deletes folder
		/// </summary>
		private bool DeleteExistingAddonFolder(PackageInfo pkg)
		{
			SetDetailMessage(pkg.guid, "Deleting addon...");

			string[] guids = AssetDatabase.FindAssets(pkg.guid);
			if (guids.Length == 0)
			{
				Debug.Log($"[AddonManager] No existing assets found for {pkg.guid}");
				SetDetailMessage(pkg.guid, $"{pkg.name} is not installed.");
				return false;
			}

		    string assetPath = AssetDatabase.GUIDToAssetPath(guids[0]);
			if (string.IsNullOrEmpty(assetPath))
			{
				Debug.LogWarning($"[AddonManager] Found {pkg.guid} but got empty path.");
				SetDetailMessage(pkg.guid, $"Delete failed: could not locate {pkg.name}.");
				return false;
			}

			string folderPath = System.IO.Path.GetDirectoryName(assetPath).Replace("\\", "/").TrimEnd('/'); // Unity uses forward slashes
			if (!AssetDatabase.IsValidFolder(folderPath))
			{
				Debug.LogError($"[AddonManager] Derived path is not a valid folder: {folderPath}");
				SetDetailMessage(pkg.guid, $"Delete failed: {folderPath} is not a folder.");
				return false;
			}

			bool success = AssetDatabase.MoveAssetToTrash(folderPath);
			if (success)
			{
				pkg.installedVersion = null;
				UpdatePackageStatus(pkg);
				InvalidateFilterCache();
				SetDetailMessage(pkg.guid, $"{pkg.name} moved to trash.");
			}
			else
			{
				Debug.LogError($"[AddonManager] Failed to delete folder: {folderPath}");
				SetDetailMessage(pkg.guid, $"Delete failed: could not move {folderPath} to trash.");
			}

			return success;
		}
	}
}
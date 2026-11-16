/**
 * AddonManager
 * Author: Denarii Games
 * Version: 1.1
 */

using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;

namespace OpenMMORPG.AddonManager
{
	public static class Constants
	{
		public static IReadOnlyList<string> Categories { get; } = new List<string>
		{
			"Demos",
			"Characters",
			"Monsters",
			"NPCs",
			"Combat",
			"Economy",
			"Items",
			"Gameplay",
			"UI",
			"MMO",
			"Tools"
		}.AsReadOnly();

		public static bool IsCategoryAllowed(string category) => Categories.Contains(category);
	}

	public enum PackageStatus { Unknown, NotInstalled, UpToDate, Outdated, Fetching }

	public class PackageInfo
	{
		public string guid; //required in package.json, must match file in packaged unitypackage
		public string name; //required in package.json
		public string packageUrl; //required in package.json
		public string latestVersion; //required in package.json
		public string updateDate; //required in package.json, format: "2025-12-25"
		public string author; //required in package.json

		public string description; //optional in package.json
		public string category;  //optional in package.json
		public bool isCore; //reserved for official addons

		public string gitUrl; //set from manifest to package.json (avoid master vs main discovery)
		public string screenshotUrl; //computed from giturl and screenshot in package.json
		public PackageStatus status = PackageStatus.Unknown; //set in GetInstalledAddons
		public string installedVersion = null; //set in GetInstalledAddons
		public Texture2D screenshot = null; //set in FetchScreenshot
	}

    public partial class AddonManagerWindow : EditorWindow
    {
		private const string PACKAGE_MANIFEST_URL = "https://raw.githubusercontent.com/open-mmorpg/open-mmorpg-addon-manager/refs/heads/master/manifest.json";
		private const string ADDON_FOLDER = "Assets/OpenMMORPG_addons/";

		private List<PackageInfo> packages = new List<PackageInfo>();
		private PackageInfo selectedPackage = null;

		[MenuItem("Tools/Open MMORPG/Develop/Addon Manager", false, -1000)]
		public static void ShowWindow()
		{
			var window = GetWindow<AddonManagerWindow>("Addon Manager");

			// Floating windows open tiny by default; give the list and details room.
			if (!window.docked && (window.position.width < DEFAULT_SIZE.x || window.position.height < DEFAULT_SIZE.y))
			{
				Rect main = EditorGUIUtility.GetMainWindowPosition();
				Vector2 size = new Vector2(
					Mathf.Max(window.position.width, DEFAULT_SIZE.x),
					Mathf.Max(window.position.height, DEFAULT_SIZE.y));
				window.position = new Rect(main.center - size / 2, size);
			}
		}

		private static readonly Vector2 MIN_SIZE = new Vector2(600, 400);
		private static readonly Vector2 DEFAULT_SIZE = new Vector2(920, 620);

		private Texture2D logoIcon;
		private Texture2D checkIcon;
		private Texture2D updateIcon;
		private Texture2D reloadIcon;
		private Texture2D coreBadge;
		private Texture2D newBadge;
		private Texture2D hotBadge;
		private string lastCategoryFilter = "";
		private string lastStatusFilter = "";
		private string lastUpdatedFilter = "";
		private string lastCoreFilter = "";
		private void OnEnable()
		{
			minSize = MIN_SIZE;
			wantsMouseMove = true; // row hover highlight

			//Load textures
			logoIcon = Resources.Load<Texture2D>("OpenMMORPG");
			checkIcon = Resources.Load<Texture2D>("CheckIcon");
			updateIcon = Resources.Load<Texture2D>("UpdateIcon");
			reloadIcon = Resources.Load<Texture2D>("ReloadIcon");
			coreBadge = Resources.Load<Texture2D>("CoreBadge");
			newBadge = Resources.Load<Texture2D>("NewBadge");
			hotBadge = Resources.Load<Texture2D>("HotBadge");

			LoadAddonsManifest();

			if (AddonInstallState.HasPending)
			{
				//CompleteInstall clears the pending state once it actually finishes
				CompleteInstall(AddonInstallState.PackageGuid, ADDON_FOLDER + AddonInstallState.TargetFolder);
			}

			//clear filters
			lastCategoryFilter = "";
			lastStatusFilter = "";
			lastUpdatedFilter = "";
			lastCoreFilter = "";
		}
	}
}
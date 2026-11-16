/**
 * AddonManager.UI
 * Author: Denarii Games
 * Version: 1.1
 *
 * UI related functionality.
 *
 * Layout: filter toolbar across the top, a resizable addon list on the left
 * and the selected addon's details filling the rest of the window.
 */

using UnityEngine;
using UnityEditor;
using UnityEngine.Networking;
using System;
using System.Collections.Generic;

namespace OpenMMORPG.AddonManager
{
    public partial class AddonManagerWindow
    {
		private const float LIST_MIN_WIDTH = 220f;
		private const float DETAIL_MIN_WIDTH = 320f;
		private const float LIST_DEFAULT_WIDTH = 300f;
		private const float ROW_HEIGHT = 40f;
		private const float SPLITTER_WIDTH = 4f;
		private const float DETAIL_PADDING = 14f;
		private const float SCREENSHOT_MAX_HEIGHT = 420f;
		private const string LIST_WIDTH_PREF = "OpenMMORPG.AddonManager.ListWidth";

		private Vector2 scrollPosition;
		private Vector2 detailScrollPosition;
		private float listWidth = -1f;
		private bool scrollSelectionIntoView;
		private PackageInfo pendingSelection;
		[SerializeField] private string selectedGuid; // survives the domain reload an install triggers
		private PackageInfo hoveredPackage;
		private readonly HashSet<PackageInfo> screenshotsFetching = new HashSet<PackageInfo>();

		/// <summary>
		/// Main entry point to UI.
		/// </summary>
		private void OnGUI()
		{
			if (uiManifestError != null)
			{
				DrawCenteredBox(() =>
				{
					EditorGUILayout.HelpBox($"Error loading addons list: {uiManifestError}", MessageType.Error);
					GUILayout.Space(20);
					EditorGUILayout.BeginHorizontal();
					GUILayout.FlexibleSpace();
					if (GUILayout.Button("Retry", GUILayout.Width(150), GUILayout.Height(30)))
					{
						LoadAddonsManifest();
					}
					GUILayout.FlexibleSpace();
					EditorGUILayout.EndHorizontal();
				});
				return;
			}

			if (!isManifestLoaded)
			{
				DrawCenteredBox(() =>
				{
					EditorGUILayout.BeginVertical();
					if (logoIcon != null)
					{
						EditorGUILayout.BeginHorizontal();
						GUILayout.FlexibleSpace();
						GUILayout.Label(logoIcon, GUILayout.Width(64), GUILayout.Height(64));
						GUILayout.FlexibleSpace();
						EditorGUILayout.EndHorizontal();
					}
					EditorGUILayout.BeginHorizontal();
					GUILayout.FlexibleSpace();
					GUILayout.Label("Loading addons list...");
					GUILayout.FlexibleSpace();
					EditorGUILayout.EndHorizontal();
					EditorGUILayout.EndVertical();
				});
				return;
			}

			if (packages.Count == 0)
			{
				DrawCenteredBox(() =>
				{
					EditorGUILayout.HelpBox("No addons available. Check your internet connection.", MessageType.Error);
				});
				return;
			}

			List<PackageInfo> filteredPackages = GetFilteredPackages();

			// Selection only changes at the start of a Layout pass so the detail
			// panel's layout never changes between Layout and the events after it.
			if (Event.current.type == EventType.Layout)
			{
				if (pendingSelection != null)
				{
					SelectPackage(pendingSelection);
					pendingSelection = null;
				}
				else if (filteredPackages.Count > 0 && (selectedPackage == null || !filteredPackages.Contains(selectedPackage)))
				{
					// Keep the same addon selected across a list or domain reload, else pick the first one
					PackageInfo same = string.IsNullOrEmpty(selectedGuid) ? null
						: filteredPackages.Find(p => p.guid == selectedGuid);
					SelectPackage(same ?? filteredPackages[0]);
				}
			}

			DrawToolbar(filteredPackages.Count);

			float top = EditorStyles.toolbar.fixedHeight;
			Rect body = new Rect(0, top, position.width, position.height - top);

			if (listWidth < 0)
				listWidth = EditorPrefs.GetFloat(LIST_WIDTH_PREF, LIST_DEFAULT_WIDTH);
			listWidth = Mathf.Clamp(listWidth, LIST_MIN_WIDTH, Mathf.Max(LIST_MIN_WIDTH, body.width - DETAIL_MIN_WIDTH));

			Rect listRect = new Rect(body.x, body.y, listWidth, body.height);
			Rect splitterRect = new Rect(listRect.xMax, body.y, SPLITTER_WIDTH, body.height);
			Rect detailRect = new Rect(splitterRect.xMax, body.y, body.width - splitterRect.xMax, body.height);

			// Splitter first so it gets the mouse before the list rows next to it
			DrawSplitter(splitterRect);
			DrawPackageList(listRect, filteredPackages);
			DrawDetailPanel(detailRect, filteredPackages.Count);
		}

		private void SelectPackage(PackageInfo pkg)
		{
			if (selectedPackage == pkg) return;

			// A reloaded copy of the same addon keeps its scroll position
			if (pkg == null || pkg.guid != selectedGuid)
				detailScrollPosition = Vector2.zero;

			selectedPackage = pkg;
			selectedGuid = pkg?.guid;
			FetchScreenshot(pkg);
		}

		/// <summary>
		/// Shows install/delete feedback on the addon it is about, so browsing other
		/// addons while one installs neither hides nor misattributes its progress.
		/// </summary>
		private void SetDetailMessage(string guid, string message)
		{
			uiDetailMessageGuid = guid;
			uiDetailMessage = message;
			Repaint();
		}

		/// <summary>
		/// Filter dropdowns, addon count and reload button.
		/// </summary>
		private void DrawToolbar(int visibleCount)
		{
			GUILayout.BeginHorizontal(EditorStyles.toolbar);
			{
				// --- Category Filter ---
				if (GUILayout.Button(currentCategoryFilter, EditorStyles.toolbarDropDown, GUILayout.Width(120)))
				{
					GenericMenu menu = new GenericMenu();

					// Always include "All Categories"
					menu.AddItem(new GUIContent("All Categories"), currentCategoryFilter == "All Categories", () =>
					{
						currentCategoryFilter = "All Categories";
						Repaint();
					});

					foreach (string cat in Constants.Categories)
					{
						string catCapture = cat; // Capture for closure
						menu.AddItem(new GUIContent(cat), currentCategoryFilter == cat, () =>
						{
							currentCategoryFilter = catCapture;
							Repaint();
						});
					}

					menu.DropDown(GUILayoutUtility.GetLastRect());
				}

				// --- Core Filter ---
				if (GUILayout.Button(currentCoreFilter, EditorStyles.toolbarDropDown, GUILayout.Width(110)))
				{
					GenericMenu menu = new GenericMenu();

					string[] options = { "All Publishers", "Core", "Community" };
					foreach (string option in options)
					{
						string capture = option;
						menu.AddItem(new GUIContent(option), currentCoreFilter == option, () =>
						{
							currentCoreFilter = capture;
							Repaint();
						});
					}

					menu.DropDown(GUILayoutUtility.GetLastRect());
				}

				// --- Update Filter ---
				if (GUILayout.Button(currentUpdatedFilter, EditorStyles.toolbarDropDown, GUILayout.Width(95)))
				{
					GenericMenu menu = new GenericMenu();

					string[] options = { "Anytime", "This week", "This month" };
					foreach (string option in options)
					{
						string capture = option;
						menu.AddItem(new GUIContent(option), currentUpdatedFilter == option, () =>
						{
							currentUpdatedFilter = capture;
							Repaint();
						});
					}

					menu.DropDown(GUILayoutUtility.GetLastRect());
				}

				// --- Status Filter ---
				if (GUILayout.Button(currentStatusFilter, EditorStyles.toolbarDropDown, GUILayout.Width(125)))
				{
					GenericMenu menu = new GenericMenu();

					string[] options = { "All Addons", "Installed", "Update Available", "Not Installed" };
					foreach (string option in options)
					{
						string capture = option;
						menu.AddItem(new GUIContent(option), currentStatusFilter == option, () =>
						{
							currentStatusFilter = capture;
							Repaint();
						});
					}

					menu.DropDown(GUILayoutUtility.GetLastRect());
				}

				GUILayout.FlexibleSpace();

				GUILayout.Label($"{visibleCount} addon{(visibleCount == 1 ? "" : "s")}", EditorStyles.miniLabel);

				if (reloadIcon != null)
				{
					if (GUILayout.Button(new GUIContent(reloadIcon, "Reload addons list"), EditorStyles.toolbarButton, GUILayout.Width(28)))
					{
						LoadAddonsManifest();
					}
				}
			}
			GUILayout.EndHorizontal();
		}

		/// <summary>
		/// Left-hand list of addons. Click or use the arrow keys to select.
		/// </summary>
		private void DrawPackageList(Rect area, List<PackageInfo> filteredPackages)
		{
			Event evt = Event.current;

			// Arrow keys move the selection
			if (evt.type == EventType.KeyDown && filteredPackages.Count > 0 &&
				(evt.keyCode == KeyCode.UpArrow || evt.keyCode == KeyCode.DownArrow))
			{
				int index = filteredPackages.IndexOf(pendingSelection ?? selectedPackage);
				index += evt.keyCode == KeyCode.DownArrow ? 1 : -1;
				pendingSelection = filteredPackages[Mathf.Clamp(index, 0, filteredPackages.Count - 1)];
				scrollSelectionIntoView = true;
				evt.Use();
				Repaint();
			}

			if (evt.type == EventType.Repaint)
				EditorGUI.DrawRect(area, ListBackgroundColor);

			if (filteredPackages.Count == 0)
			{
				Rect msgRect = new Rect(area.x + 10, area.y + 10, area.width - 20, 40);
				GUI.Label(msgRect, "No addons match the current filters.", EditorStyles.wordWrappedMiniLabel);
				return;
			}

			float contentHeight = filteredPackages.Count * ROW_HEIGHT;
			bool hasScrollbar = contentHeight > area.height;
			Rect viewRect = new Rect(0, 0, area.width - (hasScrollbar ? GUI.skin.verticalScrollbar.fixedWidth : 0), contentHeight);

			if (scrollSelectionIntoView && evt.type == EventType.Repaint)
			{
				int index = filteredPackages.IndexOf(selectedPackage);
				if (index >= 0)
				{
					float rowTop = index * ROW_HEIGHT;
					if (rowTop < scrollPosition.y)
						scrollPosition.y = rowTop;
					else if (rowTop + ROW_HEIGHT > scrollPosition.y + area.height)
						scrollPosition.y = rowTop + ROW_HEIGHT - area.height;
				}
				scrollSelectionIntoView = false;
			}

			PackageInfo hovered = null;
			scrollPosition = GUI.BeginScrollView(area, scrollPosition, viewRect);
			{
				// Only draw rows that are on screen
				int first = Mathf.Max(0, Mathf.FloorToInt(scrollPosition.y / ROW_HEIGHT));
				int last = Mathf.Min(filteredPackages.Count - 1, Mathf.CeilToInt((scrollPosition.y + area.height) / ROW_HEIGHT));

				for (int i = first; i <= last; i++)
				{
					PackageInfo pkg = filteredPackages[i];
					Rect rowRect = new Rect(0, i * ROW_HEIGHT, viewRect.width, ROW_HEIGHT);

					if (rowRect.Contains(evt.mousePosition))
					{
						hovered = pkg;

						if (evt.type == EventType.MouseDown && evt.button == 0)
						{
							pendingSelection = pkg;
							GUI.FocusControl(null);
							evt.Use();
							Repaint();
						}
					}

					if (evt.type == EventType.Repaint)
						DrawPackageRow(rowRect, pkg, pkg == selectedPackage, pkg == hoveredPackage);
				}
			}
			GUI.EndScrollView();

			if (evt.type == EventType.MouseMove || evt.type == EventType.MouseLeaveWindow)
			{
				if (evt.type == EventType.MouseLeaveWindow || !area.Contains(evt.mousePosition))
					hovered = null;

				if (hovered != hoveredPackage)
				{
					hoveredPackage = hovered;
					Repaint();
				}
			}
		}

		private void DrawPackageRow(Rect rect, PackageInfo pkg, bool selected, bool hovered)
		{
			if (selected)
				EditorGUI.DrawRect(rect, SelectionColor);
			else if (hovered)
				EditorGUI.DrawRect(rect, HoverColor);

			// Divider
			EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 1, rect.width, 1), DividerColor);

			float x = rect.x + 10;
			float right = rect.xMax - 8;

			// --- Status Icon (right) ---
			Texture2D statusIcon = pkg.status == PackageStatus.UpToDate ? checkIcon :
								   pkg.status == PackageStatus.Outdated ? updateIcon : null;
			if (statusIcon != null)
				GUI.DrawTexture(new Rect(right - 16, rect.y + (rect.height - 16) / 2, 16, 16), statusIcon, ScaleMode.ScaleToFit);
			right -= 22;

			// --- Version (right) ---
			string versionDisplay = string.IsNullOrEmpty(pkg.latestVersion) || pkg.latestVersion == "Fetching..." ? "—" : pkg.latestVersion;
			GUIStyle versionStyle = selected ? RowVersionSelectedStyle : RowVersionStyle;
			float versionWidth = Mathf.Min(versionStyle.CalcSize(new GUIContent(versionDisplay)).x, 80);
			versionStyle.Draw(new Rect(right - versionWidth, rect.y, versionWidth, rect.height), versionDisplay, false, false, false, false);
			right -= versionWidth + 6;

			// --- Badges ---
			if (pkg.isCore && coreBadge != null)
			{
				GUI.DrawTexture(new Rect(x, rect.y + 5, 14, 14), coreBadge, ScaleMode.ScaleToFit);
				x += 17;
			}
			if (IsNewThisWeek(pkg.updateDate) && newBadge != null)
			{
				GUI.DrawTexture(new Rect(x, rect.y + 5, 14, 14), newBadge, ScaleMode.ScaleToFit);
				x += 17;
			}

			// --- Name and subtitle ---
			(selected ? RowTitleSelectedStyle : RowTitleStyle).Draw(new Rect(x, rect.y + 3, Mathf.Max(0, right - x), 18), pkg.name, false, false, false, false);

			string subtitle = string.IsNullOrEmpty(pkg.category) ? pkg.author : $"{pkg.category} · {pkg.author}";
			float subtitleX = rect.x + 10;
			(selected ? RowSubtitleSelectedStyle : RowSubtitleStyle).Draw(
				new Rect(subtitleX, rect.y + 21, Mathf.Max(0, right - subtitleX), 16), subtitle, false, false, false, false);
		}

		/// <summary>
		/// Drag handle between the list and the detail panel.
		/// </summary>
		private void DrawSplitter(Rect rect)
		{
			Event evt = Event.current;
			int controlId = GUIUtility.GetControlID(FocusType.Passive);
			Rect hitRect = new Rect(rect.x - 2, rect.y, rect.width + 4, rect.height);
			EditorGUIUtility.AddCursorRect(hitRect, MouseCursor.ResizeHorizontal, controlId);

			switch (evt.GetTypeForControl(controlId))
			{
				case EventType.MouseDown when evt.button == 0 && hitRect.Contains(evt.mousePosition):
					GUIUtility.hotControl = controlId; // keep receiving drag events outside the window
					evt.Use();
					break;
				case EventType.MouseDrag when GUIUtility.hotControl == controlId:
					listWidth = evt.mousePosition.x;
					evt.Use();
					Repaint();
					break;
				case EventType.MouseUp when GUIUtility.hotControl == controlId:
					GUIUtility.hotControl = 0;
					EditorPrefs.SetFloat(LIST_WIDTH_PREF, listWidth);
					evt.Use();
					break;
				case EventType.Repaint:
					EditorGUI.DrawRect(new Rect(rect.x, rect.y, 1, rect.height), DividerColor);
					break;
			}
		}

		private void DrawDetailPanel(Rect area, int visibleCount)
		{
			GUILayout.BeginArea(area);
			if (selectedPackage != null && visibleCount > 0)
			{
				AddonDetailView(area.width);
			}
			else
			{
				GUILayout.Space(DETAIL_PADDING);
				GUILayout.BeginHorizontal();
				GUILayout.Space(DETAIL_PADDING);
				EditorGUILayout.HelpBox(visibleCount == 0
					? "No addons match the current filters."
					: "Select an addon to view details.", MessageType.Info);
				GUILayout.Space(DETAIL_PADDING);
				GUILayout.EndHorizontal();
			}
			GUILayout.EndArea();
		}

		/// <summary>
		/// Detail view for selected addon
		/// </summary>
		private string uiDetailMessage = "";
		private string uiDetailMessageGuid;
		private void AddonDetailView(float panelWidth)
		{
			PackageInfo pkg = selectedPackage;

			// Vertical scrolling only; content is laid out to the panel width
			detailScrollPosition = EditorGUILayout.BeginScrollView(detailScrollPosition, false, false,
				GUIStyle.none, GUI.skin.verticalScrollbar, GUIStyle.none);
			EditorGUILayout.BeginVertical(DetailPaddingStyle);

			// Width the content can use without a horizontal scrollbar
			float contentWidth = Mathf.Max(100, panelWidth - DETAIL_PADDING * 2 - GUI.skin.verticalScrollbar.fixedWidth);

			// Installation feedback
			if (!string.IsNullOrEmpty(uiDetailMessage) && uiDetailMessageGuid == pkg.guid)
			{
				MessageType msgType = uiDetailMessage.ToLower().Contains("fail") || uiDetailMessage.ToLower().Contains("error")
					? MessageType.Error : MessageType.Info;
				EditorGUILayout.HelpBox(uiDetailMessage, msgType);
				EditorGUILayout.Space(6);
			}

			// --- Title ---
			GUILayout.Label(pkg.name, HeaderStyle, GUILayout.MaxWidth(contentWidth));

			// --- Author, category and badges ---
			GUILayout.BeginHorizontal();
			{
				string byline = $"by {pkg.author}";
				if (!string.IsNullOrEmpty(pkg.category))
					byline += $"  ·  {pkg.category}";
				GUILayout.Label(byline, SubtleLabelStyle, GUILayout.ExpandWidth(false));

				if (pkg.isCore && coreBadge != null)
					DrawBadge(coreBadge, "Core");
				if (IsNewThisWeek(pkg.updateDate) && newBadge != null)
					DrawBadge(newBadge, "New this week");

				GUILayout.FlexibleSpace();
			}
			GUILayout.EndHorizontal();

			EditorGUILayout.Space(4);

			// --- Version and status ---
			string latestColor = pkg.status == PackageStatus.UpToDate ? (EditorGUIUtility.isProSkin ? "#4CC38A" : "#05714B") :
								 pkg.status == PackageStatus.Outdated ? (EditorGUIUtility.isProSkin ? "#FF6B6B" : "#DD2235") :
								 EditorGUIUtility.isProSkin ? "#E5E5E5" : "#212121";
			string statusText = pkg.status switch
			{
				PackageStatus.UpToDate => "Installed",
				PackageStatus.Outdated => string.IsNullOrEmpty(pkg.installedVersion)
					? "Update available"
					: $"Update available (installed {pkg.installedVersion})",
				PackageStatus.NotInstalled => "Not installed",
				_ => ""
			};
			string versionLine = $"<b>Version</b> <color={latestColor}>{pkg.latestVersion}</color>   ·   <b>Updated</b> {pkg.updateDate}";
			if (!string.IsNullOrEmpty(statusText))
				versionLine += $"   ·   {statusText}";
			GUILayout.Label(versionLine, RichWrappedLabelStyle, GUILayout.MaxWidth(contentWidth));

			EditorGUILayout.Space(10);

			// --- Actions ---
			GUILayout.BeginHorizontal();
			{
				using (new EditorGUI.DisabledScope(AddonInstallState.HasPending))
				{
					string buttonLabel = pkg.status switch
					{
						PackageStatus.NotInstalled => "Install",
						PackageStatus.Outdated     => "Update",
						_                          => "Reinstall"
					};

					if (GUILayout.Button(buttonLabel, GUILayout.Width(110), GUILayout.Height(26)))
					{
						InstallOrUpdatePackage(pkg);
					}

					if (pkg.status != PackageStatus.NotInstalled)
					{
						if (GUILayout.Button("Delete", GUILayout.Width(90), GUILayout.Height(26)))
						{
							DeleteExistingAddonFolder(pkg);
						}
					}
				}

				GUILayout.Space(8);

				if (!string.IsNullOrEmpty(pkg.gitUrl))
				{
					GUILayout.BeginVertical();
					GUILayout.Space(5);
					if (EditorGUILayout.LinkButton("View on GitHub"))
					{
						Application.OpenURL(GetRepositoryBaseUrl(pkg.gitUrl));
					}
					GUILayout.EndVertical();
				}

				GUILayout.FlexibleSpace();
			}
			GUILayout.EndHorizontal();

			EditorGUILayout.Space(12);
			Rect lineRect = GUILayoutUtility.GetRect(contentWidth, 1, GUILayout.ExpandWidth(true));
			if (Event.current.type == EventType.Repaint)
				EditorGUI.DrawRect(lineRect, DividerColor);
			EditorGUILayout.Space(12);

			// --- Screenshot, scaled to the panel width ---
			if (pkg.screenshot != null)
			{
				Texture2D tex = pkg.screenshot;
				float aspect = tex.width > 0 ? (float)tex.height / tex.width : 0.5625f;
				float width = Mathf.Min(contentWidth, tex.width);
				float height = width * aspect;
				if (height > SCREENSHOT_MAX_HEIGHT)
				{
					height = SCREENSHOT_MAX_HEIGHT;
					width = height / aspect;
				}

				Rect imageRect = GUILayoutUtility.GetRect(width, height, GUILayout.Width(width), GUILayout.Height(height));
				if (Event.current.type == EventType.Repaint)
					GUI.DrawTexture(imageRect, tex, ScaleMode.ScaleToFit);
				EditorGUILayout.Space(12);
			}
			else if (screenshotsFetching.Contains(pkg))
			{
				float height = Mathf.Min(contentWidth * 0.5625f, 240f);
				Rect placeholder = GUILayoutUtility.GetRect(contentWidth, height, GUILayout.Width(contentWidth), GUILayout.Height(height));
				if (Event.current.type == EventType.Repaint)
				{
					EditorGUI.DrawRect(placeholder, ListBackgroundColor);
					GUI.Label(placeholder, "Loading preview...", CenteredMiniLabelStyle);
				}
				EditorGUILayout.Space(12);
			}

			// --- Description ---
			if (!string.IsNullOrEmpty(pkg.description))
			{
				// Convert \n to actual newlines for proper line breaks
				string formattedDescription = pkg.description
					.Replace("\\n\\n", "\n\n")  // Double newline for paragraph spacing
					.Replace("\\n", "\n");      // Single newline

				GUILayout.Label(formattedDescription, ParagraphStyle, GUILayout.MaxWidth(contentWidth));
			}
			else
			{
				GUILayout.Label("No description provided.", SubtleLabelStyle);
			}

			EditorGUILayout.EndVertical();
			EditorGUILayout.EndScrollView();
		}

		private void DrawBadge(Texture2D icon, string label)
		{
			GUILayout.Space(10);
			GUILayout.BeginVertical(GUILayout.Width(14));
			GUILayout.Space(3);
			GUILayout.Label(icon, GUIStyle.none, GUILayout.Width(14), GUILayout.Height(14));
			GUILayout.EndVertical();
			GUILayout.Space(3);
			GUILayout.Label(label, SubtleLabelStyle, GUILayout.ExpandWidth(false));
		}

		/// <summary>
		/// Utility to convert uri to package.json to clickable url
		/// </summary>
		/// <param name="fullUrl"></param>
		/// <returns></returns>
		public static string GetRepositoryBaseUrl(string fullUrl)
		{
			Uri uri = new Uri(fullUrl);

			// Get everything up to and including the repo name
			string path = uri.AbsolutePath;

			// Split the path and take only the owner and repo parts
			string[] parts = path.Trim('/').Split('/');

			if (parts.Length >= 2)
			{
				string owner = parts[0];
				string repo = parts[1];

				// Reconstruct the base URL
				return $"https://github.com/{owner}/{repo}";
			}

			// Fallback (shouldn't happen with valid GitHub URLs)
			return fullUrl;
		}

		/// <summary>
		/// Caches pkg.screenshot from pkg.screenshotUrl
		/// </summary>
		/// <param name="pkg"></param>
		private void FetchScreenshot(PackageInfo pkg)
		{
			if (pkg == null || pkg.screenshot != null || string.IsNullOrEmpty(pkg.screenshotUrl)) return;
			if (!screenshotsFetching.Add(pkg)) return; // already downloading

			UnityWebRequest www = UnityWebRequestTexture.GetTexture(pkg.screenshotUrl);
			var asyncOp = www.SendWebRequest();

			EditorApplication.CallbackFunction updateCallback = null;

			updateCallback = () =>
			{
				if (asyncOp.isDone)
				{
					EditorApplication.update -= updateCallback;
					screenshotsFetching.Remove(pkg);

					if (www.result == UnityWebRequest.Result.Success)
					{
						pkg.screenshot = ((DownloadHandlerTexture)www.downloadHandler).texture;
					}
					else
					{
						Debug.LogWarning($"[AddonManager {Time.time}] failed to fetch screenshot for {pkg.name}: {www.error}");
					}

					www.Dispose();
					Repaint();
				}
			};

			EditorApplication.update += updateCallback;
		}

		private bool IsNewThisWeek(string updateDateStr)
		{
			var updateDt = ParseUpdateDate(updateDateStr);
			if (!updateDt.HasValue) return false;

			System.DateTime today = System.DateTime.Today;
			int daysSinceMonday = ((int)today.DayOfWeek + 6) % 7; // Monday = 0
			System.DateTime startOfThisWeek = today.AddDays(-daysSinceMonday);

			return updateDt.Value >= startOfThisWeek;
		}

		private void DrawCenteredBox(Action content, float dialogWidth = 350f, float dialogHeight = 180f)
		{
			// Full window rect
			Rect fullRect = new Rect(0, 0, position.width, position.height);

			Rect centeredRect = new Rect(
				(fullRect.width - dialogWidth) / 2,
				(fullRect.height - dialogHeight) / 2,
				dialogWidth,
				dialogHeight
			);

			GUILayout.BeginArea(centeredRect);
			content?.Invoke();
			GUILayout.EndArea();
		}

		// ---------------------------------------------------------------- Colors

		private static Color ListBackgroundColor => EditorGUIUtility.isProSkin
			? new Color(0.165f, 0.165f, 0.165f)
			: new Color(0.87f, 0.87f, 0.87f);

		private static Color SelectionColor => EditorGUIUtility.isProSkin
			? new Color(0.173f, 0.365f, 0.529f)
			: new Color(0.227f, 0.447f, 0.690f);

		private static Color HoverColor => EditorGUIUtility.isProSkin
			? new Color(1f, 1f, 1f, 0.06f)
			: new Color(0f, 0f, 0f, 0.06f);

		private static Color DividerColor => EditorGUIUtility.isProSkin
			? new Color(0f, 0f, 0f, 0.35f)
			: new Color(0f, 0f, 0f, 0.15f);

		private static Color TextColor => EditorGUIUtility.isProSkin
			? new Color(0.898f, 0.898f, 0.898f)  // #E5E5E5
			: new Color(0.129f, 0.129f, 0.129f); // #212121

		// ---------------------------------------------------------------- Styles
		// Text colors are refreshed on every access in case the user switched theme.

		private static GUIStyle headerStyle;
		private static GUIStyle HeaderStyle
		{
			get
			{
				if (headerStyle == null)
				{
					headerStyle = new GUIStyle(EditorStyles.boldLabel)
					{
						fontSize = 20,
						richText = true,
						wordWrap = true
					};
				}

				headerStyle.normal.textColor = TextColor;
				return headerStyle;
			}
		}

		private static GUIStyle paragraphStyle;
		private static GUIStyle ParagraphStyle
		{
			get
			{
				if (paragraphStyle == null)
				{
					paragraphStyle = new GUIStyle(EditorStyles.wordWrappedLabel)
					{
						fontSize = 13,
						richText = true
					};
				}

				paragraphStyle.normal.textColor = TextColor;
				return paragraphStyle;
			}
		}

		private static GUIStyle subtleLabelStyle;
		private static GUIStyle SubtleLabelStyle
		{
			get
			{
				if (subtleLabelStyle == null)
				{
					subtleLabelStyle = new GUIStyle(EditorStyles.label) { fontSize = 12 };
				}

				subtleLabelStyle.normal.textColor = EditorGUIUtility.isProSkin
					? new Color(0.68f, 0.68f, 0.68f)
					: new Color(0.35f, 0.35f, 0.35f);
				return subtleLabelStyle;
			}
		}

		private static GUIStyle richWrappedLabelStyle;
		private static GUIStyle RichWrappedLabelStyle
		{
			get
			{
				if (richWrappedLabelStyle == null)
				{
					richWrappedLabelStyle = new GUIStyle(EditorStyles.wordWrappedLabel) { richText = true };
				}

				richWrappedLabelStyle.normal.textColor = TextColor;
				return richWrappedLabelStyle;
			}
		}

		private static GUIStyle centeredMiniLabelStyle;
		private static GUIStyle CenteredMiniLabelStyle
		{
			get
			{
				if (centeredMiniLabelStyle == null)
				{
					centeredMiniLabelStyle = new GUIStyle(EditorStyles.centeredGreyMiniLabel);
				}

				return centeredMiniLabelStyle;
			}
		}

		private static GUIStyle detailPaddingStyle;
		private static GUIStyle DetailPaddingStyle
		{
			get
			{
				if (detailPaddingStyle == null)
				{
					int pad = (int)DETAIL_PADDING;
					detailPaddingStyle = new GUIStyle { padding = new RectOffset(pad, pad, pad - 2, pad) };
				}

				return detailPaddingStyle;
			}
		}

		private static GUIStyle rowTitleStyle;
		private static GUIStyle RowTitleStyle
		{
			get
			{
				if (rowTitleStyle == null)
				{
					rowTitleStyle = new GUIStyle(EditorStyles.boldLabel)
					{
						fontSize = 12,
						clipping = TextClipping.Clip,
						alignment = TextAnchor.MiddleLeft
					};
				}

				rowTitleStyle.normal.textColor = TextColor;
				return rowTitleStyle;
			}
		}

		private static GUIStyle rowTitleSelectedStyle;
		private static GUIStyle RowTitleSelectedStyle
		{
			get
			{
				if (rowTitleSelectedStyle == null)
				{
					rowTitleSelectedStyle = new GUIStyle(RowTitleStyle);
				}

				rowTitleSelectedStyle.normal.textColor = Color.white;
				return rowTitleSelectedStyle;
			}
		}

		private static GUIStyle rowSubtitleStyle;
		private static GUIStyle RowSubtitleStyle
		{
			get
			{
				if (rowSubtitleStyle == null)
				{
					rowSubtitleStyle = new GUIStyle(EditorStyles.miniLabel)
					{
						clipping = TextClipping.Clip,
						alignment = TextAnchor.MiddleLeft
					};
				}

				rowSubtitleStyle.normal.textColor = EditorGUIUtility.isProSkin
					? new Color(0.62f, 0.62f, 0.62f)
					: new Color(0.38f, 0.38f, 0.38f);
				return rowSubtitleStyle;
			}
		}

		private static GUIStyle rowSubtitleSelectedStyle;
		private static GUIStyle RowSubtitleSelectedStyle
		{
			get
			{
				if (rowSubtitleSelectedStyle == null)
				{
					rowSubtitleSelectedStyle = new GUIStyle(RowSubtitleStyle);
				}

				rowSubtitleSelectedStyle.normal.textColor = new Color(0.85f, 0.9f, 0.95f);
				return rowSubtitleSelectedStyle;
			}
		}

		private static GUIStyle rowVersionStyle;
		private static GUIStyle RowVersionStyle
		{
			get
			{
				if (rowVersionStyle == null)
				{
					rowVersionStyle = new GUIStyle(EditorStyles.miniLabel)
					{
						alignment = TextAnchor.MiddleRight,
						fontSize = 11
					};
				}

				rowVersionStyle.normal.textColor = RowSubtitleStyle.normal.textColor;
				return rowVersionStyle;
			}
		}

		private static GUIStyle rowVersionSelectedStyle;
		private static GUIStyle RowVersionSelectedStyle
		{
			get
			{
				if (rowVersionSelectedStyle == null)
				{
					rowVersionSelectedStyle = new GUIStyle(RowVersionStyle);
				}

				rowVersionSelectedStyle.normal.textColor = new Color(0.85f, 0.9f, 0.95f);
				return rowVersionSelectedStyle;
			}
		}
	}
}

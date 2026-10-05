using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using JSI;
using UnityEngine;

namespace RPMusicPlayer
{
    /// <summary>
    /// Adds the browser and player pages to every RasterPropMonitor in the scene.
    ///
    /// The pages are built in code rather than shipped as part config, so the plugin
    /// works with any pod or IVA mod that has a monitor, including ones released after
    /// this plugin, without needing a patch for each one.
    /// </summary>
    internal static class PageInjector
    {
        internal const string BrowserPageName = "rpmusicBrowser";
        internal const string PlayerPageName = "rpmusicPlayer";

        internal const string BrowserHandlerType = "RPMusicPlayerBrowser";
        internal const string PlayerHandlerType = "RPMusicPlayerPlayer";

        private static readonly FieldInfo PagesField = typeof(RasterPropMonitor)
            .GetField("pages", BindingFlags.Instance | BindingFlags.NonPublic);

        private static readonly FieldInfo ModuleConfigField = typeof(RasterPropMonitor)
            .GetField("moduleConfig", BindingFlags.Instance | BindingFlags.NonPublic);

       

                /// <summary>The button name that opens the music pages on the last patched monitor.</summary>
                internal static string EntryButtonName { get; private set; }

                /// <summary>True once at least one monitor has had the music pages added.</summary>
                internal static bool IsPatched
                {
                    get { return Injected.Count > 0; }
                }

        /// <summary>Pages we created, per monitor instance id.</summary>
        private static readonly Dictionary<int, List<MonitorPage>> Injected = new Dictionary<int, List<MonitorPage>>();

        internal static void Reset()
        {
            Injected.Clear();
            EntryButtonName = null;
        }

        /// <summary>
        /// Walks every internal model in flight and makes sure the music pages exist
        /// on each monitor. Safe to call repeatedly; already patched monitors are skipped.
        /// Returns the number of monitors that were patched this time round.
        /// </summary>
        internal static int InjectAll()
        {
            var patched = 0;
            var vessel = FlightContext.Vessel;

            if (vessel == null || vessel.packed || !vessel.loaded)
            {
                return patched;
            }

            foreach (var part in vessel.Parts)
            {
                if (part == null || part.packed || part.internalModel == null)
                {
                    continue;
                }

                patched += InjectModel(part.internalModel);
            }

            return patched;
        }

        private static int InjectModel(InternalModel model)
        {
            var monitors = model.FindModelComponents<RasterPropMonitor>();
            if (monitors == null || monitors.Length == 0)
            {
                return 0;
            }

            Log.Info("Found {0} RasterPropMonitor(s) in internal model '{1}'.",
                monitors.Length, model.internalName);

            var patched = 0;
            foreach (var monitor in monitors)
            {
                if (Inject(monitor))
                {
                    patched++;
                }
            }

            return patched;
        }

        internal static bool Inject(RasterPropMonitor monitor)
        {
            if (monitor == null || PagesField == null)
            {
                if (PagesField == null)
                {
                    Log.Error("Could not find the RasterPropMonitor page list. This version of RPM is not supported.");
                }
                return false;
            }

            var pages = PagesField.GetValue(monitor) as IList;
            if (pages == null)
            {
                // RasterPropMonitor has not finished starting up yet; try again next frame.
                return false;
            }

            if (Injected.ContainsKey(monitor.GetInstanceID()))
            {
                return false;
            }

            // A previous visit to this vessel already added the pages. Adopt them so that the
            // pages can still find each other and the exit page.
            foreach (var entry in pages)
            {
                var existing = entry as MonitorPage;
                if (existing == null)
                {
                    continue;
                }

                if (existing.name == BrowserPageName || existing.name == PlayerPageName)
                {
                    var adopted = new List<MonitorPage>();
                    foreach (var other in pages)
                    {
                        var candidate = other as MonitorPage;
                        if (candidate != null
                            && (candidate.name == BrowserPageName || candidate.name == PlayerPageName))
                        {
                            adopted.Add(candidate);
                        }
                    }

                    Injected[monitor.GetInstanceID()] = adopted;
                    return false;
                }
            }

            try
            {
                var browser = CreatePage(monitor, pages.Count, BrowserPageName, BrowserHandlerType);
                var player = CreatePage(monitor, pages.Count + 1, PlayerPageName, PlayerHandlerType);

                if (browser == null || player == null)
                {
                    return false;
                }

                pages.Add(browser);
                pages.Add(player);

                Injected[monitor.GetInstanceID()] = new List<MonitorPage> { browser, player };

                RegisterEntryButton(monitor, browser);

                Log.Info("Music pages added to monitor in '{0}' (prop {1}).", monitor.internalModel.internalName, monitor.internalProp.propID);
                return true;
            }
            catch (Exception e)
            {
                Log.Error("Could not add the music pages to a monitor: {0}", e.Message);
                return false;
            }
        }

        private static MonitorPage CreatePage(RasterPropMonitor monitor, int pageNumber, string name, string handlerType)
        {
            var node = new ConfigNode("PAGE");
            node.AddValue("name", name);

            var handler = new ConfigNode("PAGEHANDLER");
            handler.AddValue("name", handlerType);
            handler.AddValue("method", "ShowMenu");
            handler.AddValue("pageActiveMethod", "PageActive");
            handler.AddValue("buttonClickMethod", "ClickProcessor");
            node.AddNode(handler);

            return new MonitorPage(pageNumber, node, monitor);
        }

        /// <summary>
        /// Gives the player a way in.
        ///
        /// A RasterPropMonitor page is reached by pressing a prop button, so the music
        /// pages join the page cycle of one of the monitor's own buttons, exactly like
        /// the built in modes do. Which button is configurable; by default we pick one
        /// that the pod's own pages do not already use, preferring names that suggest a
        /// menu. If nothing suitable turns up we fall back to using the screen, which is
        /// always present and otherwise does nothing.
        /// </summary>
        private static void RegisterEntryButton(RasterPropMonitor monitor, MonitorPage browser)
        {
            var configured = Settings.Current.EntryButton;

            if (string.Equals(configured, "none", StringComparison.OrdinalIgnoreCase))
            {
                Log.Info("Entry button disabled; the music pages can only be reached from another page.");
                return;
            }

            var entryName = ResolveEntryButton(monitor, configured);
            if (string.IsNullOrEmpty(entryName))
            {
                Log.Warning("No usable entry button on the '{0}' monitor.", monitor.internalModel.internalName);
                return;
            }

            try
            {
                SmarterButton.CreateButton(monitor.internalProp, entryName, browser, monitor.PageButtonClick);

                // Free IVA ignores mouse input from anything that is not on the clickable layer.
                var button = monitor.internalProp.FindModelComponent<SmarterButton>(entryName);
                if (button != null)
                {
                    button.gameObject.layer = 20;
                }

                Log.Info("Music pages added to the '{0}' monitor, opened with '{1}'.",
                    monitor.internalModel.internalName, entryName);

                EntryButtonName = entryName;
                DumpPageMap(monitor);
            }
            catch (Exception e)
            {
                Log.Warning("Could not hook up the entry button '{0}': {1}", entryName, e.Message);
            }
        }

        private static string ResolveEntryButton(RasterPropMonitor monitor, string configured)
        {
            var settings = Settings.Current;
            var globalButtons = new List<string>(ButtonMap.Split(monitor.globalButtons));

            // Naming a page is the friendliest option: the music player simply becomes
            // another mode on that button, exactly like navball and aviapfd share one.
            if (!string.IsNullOrEmpty(settings.EntryPage))
            {
                var map = PageButtonMap(monitor);
                var button = FindButtonForPage(map, settings.EntryPage);
                if (!string.IsNullOrEmpty(button))
                {
                    Log.Info("ENTRYPAGE '{0}' uses button '{1}'.", settings.EntryPage, button);
                    return button;
                }

                Log.Warning("ENTRYPAGE '{0}' did not match any page. Available pages:", settings.EntryPage);
                foreach (var pair in map)
                {
                    Log.Info("    page '{0}' -> button '{1}'", pair.Key, pair.Value);
                }
                return monitor.screenTransform;
            }

            if (!string.IsNullOrEmpty(configured)
                && !string.Equals(configured, "auto", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(configured, "screen", StringComparison.OrdinalIgnoreCase))
            {
                // An explicit choice has to name something that really exists on the prop.
                if (globalButtons.Contains(configured) || monitor.internalProp.FindModelTransform(configured) != null)
                {
                    return configured;
                }

                Log.Warning("ENTRYBUTTON '{0}' is not a transform on this monitor; ignoring it.", configured);
                return monitor.screenTransform;
            }

            if (string.Equals(configured, "screen", StringComparison.OrdinalIgnoreCase))
            {
                return monitor.screenTransform;
            }

            var chosen = PickEntryButton(monitor, globalButtons);
            return chosen ?? monitor.screenTransform;
        }

        /// <summary>
        /// Chooses where to hang the music pages. In order of preference: a button that
        /// currently drives a single page, so the music player reads as its second mode;
        /// then a menu-like global button no page uses; then any unused global button.
        /// </summary>
        private static string PickEntryButton(RasterPropMonitor monitor, List<string> globalButtons)
        {
            var map = PageButtonMap(monitor);

            var singleMode = new List<string>();
            var busy = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in map)
            {
                busy.Add(pair.Value);
            }

            foreach (var pair in map)
            {
                int uses = 0;
                foreach (var other in map)
                {
                    if (string.Equals(other.Value, pair.Value, StringComparison.OrdinalIgnoreCase))
                    {
                        uses++;
                    }
                }

                if (uses == 1 && !globalButtons.Contains(pair.Value))
                {
                    singleMode.Add(pair.Value);
                }
            }

            if (singleMode.Count > 0)
            {
                Log.Info("Adding the music player as a second mode on '{0}'.", singleMode[0]);
                return singleMode[0];
            }

            foreach (var name in globalButtons)
            {
                if (!busy.Contains(name))
                {
                    return name;
                }
            }

            return null;
        }

        private static string FindButtonForPage(List<KeyValuePair<string, string>> map, string wanted)
        {
            foreach (var pair in map)
            {
                if (string.Equals(pair.Key, wanted, StringComparison.OrdinalIgnoreCase))
                {
                    return pair.Value;
                }
            }

            // Fall back to a partial match so "data" can find a page called "database".
            foreach (var pair in map)
            {
                if (pair.Key.IndexOf(wanted, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return pair.Value;
                }
            }

            return null;
        }

        /// <summary>The monitor's pages and the button each one is bound to.</summary>
        private static List<KeyValuePair<string, string>> PageButtonMap(RasterPropMonitor monitor)
        {
            var map = new List<KeyValuePair<string, string>>();

            var node = MonitorConfigNode(monitor);
            if (node == null)
            {
                return map;
            }

            foreach (var page in node.GetNodes("PAGE"))
            {
                var name = page.GetValue("name");
                var button = page.GetValue("button");
                if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(button))
                {
                    map.Add(new KeyValuePair<string, string>(name.Trim(), button.Trim()));
                }
            }

            return map;
        }

        private static ConfigNode MonitorConfigNode(RasterPropMonitor monitor)
        {
            if (ModuleConfigField == null)
            {
                return null;
            }

            try
            {
                var holder = ModuleConfigField.GetValue(monitor) as ConfigNodeHolder;
                return holder == null ? null : holder.Node;
            }
            catch (Exception e)
            {
                Log.Info("Could not read the monitor configuration: {0}", e.Message);
                return null;
            }
        }

        /// <summary>
        /// Writes out which button drives which page, so ENTRYPAGE can be pointed at the
        /// right page without having to read the IVA's config by hand.
        /// </summary>
        private static void DumpPageMap(RasterPropMonitor monitor)
        {
            foreach (var pair in PageButtonMap(monitor))
            {
                Log.Info("    page '{0}' is on button '{1}'", pair.Key, pair.Value);
            }
        }


        /// <summary>
        /// Finds one of the pages we added, so the pages can switch to each other.
        /// </summary>
        internal static MonitorPage FindPage(RasterPropMonitor monitor, string pageName)
        {
            List<MonitorPage> pages;
            if (monitor == null || !Injected.TryGetValue(monitor.GetInstanceID(), out pages))
            {
                return null;
            }

            foreach (var page in pages)
            {
                if (page != null && page.name == pageName)
                {
                    return page;
                }
            }

            return null;
        }

        /// <summary>
        /// The page that was showing before the music pages took over, used as the
        /// place to return to when the player backs out of the browser.
        /// </summary>
        internal static MonitorPage FindExitPage(RasterPropMonitor monitor)
        {
            if (monitor == null || PagesField == null)
            {
                return null;
            }

            var pages = PagesField.GetValue(monitor) as IList;
            if (pages == null)
            {
                return null;
            }

            foreach (var entry in pages)
            {
                var page = entry as MonitorPage;
                if (page == null)
                {
                    continue;
                }

                if (page.name == BrowserPageName || page.name == PlayerPageName)
                {
                    continue;
                }

                return page;
            }

            return null;
        }

        internal static void SwitchTo(RasterPropMonitor monitor, MonitorPage target)
        {
            if (monitor == null || target == null)
            {
                return;
            }

            try
            {
                monitor.PageButtonClick(target);
            }
            catch (Exception e)
            {
                Log.Warning("Could not switch the monitor page: {0}", e.Message);
            }
        }
    }
}
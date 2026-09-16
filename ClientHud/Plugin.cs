using System;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Il2CppInterop.Runtime.Injection;
using Unity.Netcode;
using UnityEngine;

namespace WaygateMods.ClientHud
{
	// A small client-side HUD: the server's name and how many players are on it, in the top
	// left corner while you are connected. Installed on the player's PC by the Waygate app
	// when a server asks for it. Reads replicated state only; it changes nothing.
	//
	// On a server (host) this plugin does nothing.
	[BepInPlugin(PluginId, "Client HUD", Version)]
	public class ClientHudPlugin : BasePlugin
	{
		public const string PluginId = "com.humangenome.waygatemods.clienthud";
		public const string Version = "1.0.0";

		internal static ManualLogSource Logger;
		internal static ConfigEntry<bool> Show;
		internal static ConfigEntry<int> Corner;
		internal static string ServerName = "Server";
		internal static int Players = -1;
		internal static bool Connected;
		private static float sNextLog;
		private static bool sLoggedConnect;

		public override void Load()
		{
			Logger = Log;
			Show = Config.Bind("Hud", "Show", true, "Draw the server name and player count while connected.");
			Corner = Config.Bind("Hud", "Corner", 0, "0 = top left, 1 = top right.");
			ServerName = ReadServerName();
			try
			{
				ClassInjector.RegisterTypeInIl2Cpp<HudBehaviour>();
				new Harmony(PluginId).PatchAll(typeof(ClientHudPlugin).Assembly);
				Logger.LogInfo("[clienthud] " + Version + " loaded (server name '" + ServerName + "')");
			}
			catch (Exception e) { Logger.LogError("[clienthud] load failed: " + e); }
		}

		// The Waygate app writes the server's display name into the host mod's config on every
		// Connect. Read it if it is there; otherwise the HUD says "Server".
		private static string ReadServerName()
		{
			try
			{
				var path = Path.Combine(Paths.ConfigPath, "com.humangenome.waygate.host.cfg");
				if (!File.Exists(path)) return "Server";
				foreach (var raw in File.ReadAllLines(path))
				{
					var line = raw.Trim();
					if (!line.StartsWith("ServerListName", StringComparison.OrdinalIgnoreCase)) continue;
					int eq = line.IndexOf('=');
					if (eq > 0) { var v = line.Substring(eq + 1).Trim(); if (v.Length > 0) return v; }
				}
			}
			catch { }
			return "Server";
		}

		internal static bool IsPureClient()
		{
			try { var nm = NetworkManager.Singleton; return nm != null && nm.IsClient && !nm.IsServer && nm.IsConnectedClient; } catch { return false; }
		}

		// Called every frame from the menu manager's Update: keeps the numbers fresh and logs
		// them every 30 s so a headless client proves the plugin runs.
		internal static void Tick()
		{
			if (!IsPureClient()) { if (Connected) { Connected = false; sLoggedConnect = false; Logger.LogInfo("[clienthud] disconnected"); } return; }
			Connected = true;
			try
			{
				var p = DataStorage.Singleton?.Player;
				var world = p != null ? p.GetComponent<World>() : null;
				Players = world != null && world.ServerList != null ? world.ServerList.Count : -1;
			}
			catch { Players = -1; }
			float now = Time.realtimeSinceStartup;
			if (!sLoggedConnect || now >= sNextLog)
			{
				sLoggedConnect = true;
				sNextLog = now + 30f;
				Logger.LogInfo("[clienthud] on '" + ServerName + "': " + (Players < 0 ? "player count not replicated yet" : Players + " player" + (Players == 1 ? "" : "s") + " online") + (HudBehaviour.Drawn ? " (hud drawn)" : ""));
			}
			HudBehaviour.Ensure();
		}
	}

	[HarmonyPatch(typeof(TransitionManager), "Update")]
	internal static class TickPatch
	{
		private static void Postfix()
		{
			try { ClientHudPlugin.Tick(); }
			catch (Exception e) { ClientHudPlugin.Logger.LogWarning("[clienthud] tick: " + e.Message); }
		}
	}

	// The overlay itself. Unity IMGUI, drawn in OnGUI, which never runs on a headless
	// client, so that path is only ever seen by a real player.
	public class HudBehaviour : MonoBehaviour
	{
		public HudBehaviour(IntPtr ptr) : base(ptr) { }
		private static HudBehaviour sInstance;
		internal static bool Drawn;
		private GUIStyle _style;

		internal static void Ensure()
		{
			if (sInstance != null) return;
			try
			{
				var go = new GameObject("WaygateMods.ClientHud");
				UnityEngine.Object.DontDestroyOnLoad(go);
				go.hideFlags = HideFlags.HideAndDontSave;
				sInstance = go.AddComponent<HudBehaviour>();
			}
			catch (Exception e) { ClientHudPlugin.Logger.LogWarning("[clienthud] overlay: " + e.Message); }
		}

		public void OnGUI()
		{
			if (!ClientHudPlugin.Show.Value || !ClientHudPlugin.Connected) return;
			if (_style == null)
			{
				_style = new GUIStyle(GUI.skin.label);
				_style.fontSize = 14;
				_style.normal.textColor = new Color(0.9f, 0.88f, 0.84f, 0.95f);
			}
			string text = ClientHudPlugin.ServerName + "  |  " + (ClientHudPlugin.Players < 0 ? "..." : ClientHudPlugin.Players + " online");
			var size = _style.CalcSize(new GUIContent(text));
			float x = ClientHudPlugin.Corner.Value == 1 ? Screen.width - size.x - 16f : 16f;
			GUI.Label(new Rect(x, 8f, size.x + 8f, size.y + 4f), text, _style);
			Drawn = true;
		}
	}
}

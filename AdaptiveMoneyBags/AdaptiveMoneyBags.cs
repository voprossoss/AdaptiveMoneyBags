using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace AdaptiveMoneyBags
{
    [BepInPlugin("voprossoss.AdaptiveMoneyBags", "AdaptiveMoneyBags", "1.0.0")]
    public class AdaptiveMoneyBags : BaseUnityPlugin
    {
        internal static AdaptiveMoneyBags Instance { get; private set; } = null!;
        internal new static ManualLogSource Logger => Instance._logger;
        private ManualLogSource _logger => base.Logger;
        internal Harmony? Harmony { get; set; }

        private void Awake()
        {
            Instance = this;

            // Prevents the plugin from being destroyed on scene changes.
            this.gameObject.transform.parent = null;
            this.gameObject.hideFlags = HideFlags.HideAndDontSave;

            Patch();

            Logger.LogInfo($"{Info.Metadata.GUID} v{Info.Metadata.Version} has loaded!");
        }

        internal void Patch()
        {
            Harmony ??= new Harmony(Info.Metadata.GUID);
            Harmony.PatchAll();   // <-- находит ВСЕ [HarmonyPatch] в проекте и применяет их
        }

        internal void Unpatch()
        {
            Harmony?.UnpatchSelf();
        }

        private void Update()
        {
            // Code that runs every frame goes here
        }
    }
}
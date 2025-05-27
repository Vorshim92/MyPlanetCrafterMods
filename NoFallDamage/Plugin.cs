using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using SpaceCraft; // Assicurati che questo namespace esista in Assembly-CSharp o altre DLL referenziate
using System.Collections;
using UnityEngine;

namespace NoFallDamage
{
    [BepInPlugin(PLUGIN_GUID, PLUGIN_NAME, PLUGIN_VERSION)]
    public class Plugin : BaseUnityPlugin
    {
        // Definizioni per PluginInfo
        public const string PLUGIN_GUID = "Vorshim92.theplanetcrafter.nofalldamage"; // Usa il tuo AuthorName
        public const string PLUGIN_NAME = "NoFallDamage";
        public const string PLUGIN_VERSION = "1.0.0";

        private static ManualLogSource logger;
        private static ConfigEntry<bool> modEnabled;
        
        private void Awake()
        {
            logger = Logger;
            // Usa le costanti definite sopra
            logger.LogInfo($"Plugin {PLUGIN_GUID} is loaded!"); 
            
            modEnabled = Config.Bind("General", "Enabled", true, "Enable/Disable the mod");
            
            // Non è necessario controllare modEnabled qui, Harmony patcherà sempre.
            // Il controllo di modEnabled avverrà dentro i metodi patchati.
            Harmony.CreateAndPatchAll(typeof(Plugin));
        }
        
        [HarmonyPostfix]
        [HarmonyPatch(typeof(PlanetLoader), "HandleDataAfterLoad")]
        static void Patch_PlanetLoader_HandleDataAfterLoad(PlanetLoader __instance)
        {
            // Controlla se il mod è abilitato prima di avviare la coroutine
            if (modEnabled.Value)
            {
                __instance.StartCoroutine(WaitForWorldLoad(__instance));
            }
            else
            {
                logger.LogInfo("NoFallDamage mod is disabled in config, skipping fall damage removal on world load.");
            }
        }
        
        static IEnumerator WaitForWorldLoad(PlanetLoader __instance)
        {
            while (!__instance.GetIsLoaded())
            {
                yield return null;
            }
            
            yield return new WaitForSeconds(1f); // Aspetta che il player sia pronto
            
            // Non c'è bisogno di ricontrollare modEnabled.Value qui se lo fai prima di chiamare la coroutine
            DisableFallDamage();
        }
        
        [HarmonyPostfix]
        [HarmonyPatch(typeof(PlayerMainController), "OnNetworkSpawn")]
        static void Patch_PlayerMainController_OnNetworkSpawn(PlayerMainController __instance)
        {
            if (__instance.IsOwner && modEnabled.Value)
            {
                __instance.StartCoroutine(DisableFallDamageDelayed(__instance));
            }
            else if (__instance.IsOwner && !modEnabled.Value)
            {
                logger.LogInfo("NoFallDamage mod is disabled in config, skipping fall damage removal on respawn.");
            }
        }
        
        static IEnumerator DisableFallDamageDelayed(PlayerMainController player)
        {
            yield return new WaitForSeconds(0.5f); // Piccolo ritardo per assicurarsi che tutto sia inizializzato
            
            var fallDamage = player.GetComponent<PlayerFallDamage>();
            if (fallDamage != null)
            {
                fallDamage.enabled = false;
                logger.LogInfo("Fall damage component disabled for player via OnNetworkSpawn!");
            }
            else
            {
                logger.LogWarning("PlayerFallDamage component not found on player after respawn.");
            }
        }
        
        static void DisableFallDamage()
        {
            // Questa funzione viene chiamata dopo il caricamento del mondo.
            // Assicurati che modEnabled sia già stato controllato prima di chiamarla.
            var pm = Managers.GetManager<PlayersManager>();
            if (pm != null)
            {
                var player = pm.GetActivePlayerController();
                if (player != null)
                {
                    var fallDamage = player.GetComponent<PlayerFallDamage>();
                    if (fallDamage != null)
                    {
                        fallDamage.enabled = false;
                        logger.LogInfo("Fall damage component disabled via DisableFallDamage!");
                    }
                    else
                    {
                         logger.LogWarning("PlayerFallDamage component not found on active player.");
                    }
                }
                else
                {
                    logger.LogWarning("Active player controller not found in DisableFallDamage.");
                }
            }
            else
            {
                 logger.LogWarning("PlayersManager not found in DisableFallDamage.");
            }
        }
    }
}
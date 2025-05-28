using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using SpaceCraft;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BackpackExtender
{
    [BepInPlugin(PLUGIN_GUID, PLUGIN_NAME, PLUGIN_VERSION)]
    public class Plugin : BaseUnityPlugin
    {
        public const string PLUGIN_GUID = "Vorshim92.theplanetcrafter.backpackextender";
        public const string PLUGIN_NAME = "BackpackExtender";
        public const string PLUGIN_VERSION = "1.0.0";
        private static ManualLogSource logger;
        private static ConfigEntry<int> defaultBackpackSize;
        private static ConfigEntry<bool> autoSetOnSpawn;
        private static GameObject sizeControlPanel;
        private static ConfigEntry<KeyboardShortcut> togglePanelShortcut;
        private static bool showSizeControlPanelConfig = true;

        public static Plugin instance;

        private void Awake()
        {
            instance = this;
            logger = Logger;

            // Configurazione
            defaultBackpackSize = Config.Bind("General", "DefaultBackpackSize", 100, "Default backpack size when spawning");
            autoSetOnSpawn = Config.Bind("General", "AutoSetOnSpawn", true, "Automatically set backpack size on spawn");
            togglePanelShortcut = Config.Bind("General", "TogglePanelShortcut", new KeyboardShortcut(KeyCode.B, KeyCode.LeftControl, KeyCode.LeftShift), "Shortcut to toggle the backpack size control panel visibility.");
            showSizeControlPanelConfig = Config.Bind("General", "ShowSizeControlPanel", true, "Show the size control panel by default.").Value;

            Harmony.CreateAndPatchAll(typeof(Plugin));

            logger.LogInfo("BackpackExtender loaded!");
        }

        private void Update()
        {
            if (togglePanelShortcut.Value.IsDown()) // IsDown() è per un singolo evento di pressione
            {
                showSizeControlPanelConfig = !showSizeControlPanelConfig;
                logger.LogInfo($"Toggle panel shortcut pressed. Panel visible: {showSizeControlPanelConfig}");

                // Se il pannello di controllo esiste, aggiorna la sua visibilità
                if (sizeControlPanel != null)
                {
                    sizeControlPanel.SetActive(showSizeControlPanelConfig);
                    logger.LogInfo($"Size control panel active state set to: {sizeControlPanel.activeSelf}");
                }
                // Se vuoi salvare lo stato nella config:
                // Config.Bind("General", "ShowSizeControlPanel", true).Value = showSizeControlPanelConfig; // Questo riscriverebbe la descrizione, meglio avere una variabile di config dedicata.
                // Oppure, se hai una ConfigEntry<bool> per lo stato:
                // showSizeControlPanelConfigEntry.Value = showSizeControlPanelConfig; // (dove showSizeControlPanelConfigEntry è la tua ConfigEntry<bool>)
            }
        }


        private void OnDestroy()
        {
            if (sizeControlPanel != null)
            {
                Destroy(sizeControlPanel);
                sizeControlPanel = null;
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(WindowsHandler), "CloseAllWindows")]
        static void WindowsHandler_CloseAllWindows_Postfix() // Rinomina per chiarezza
        {
            if (sizeControlPanel != null)
            {
                logger.LogInfo("WindowsHandler.CloseAllWindows: Destroying size control panel.");
                Destroy(sizeControlPanel);
                sizeControlPanel = null;
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(UiWindowEquipment), "OnClose")]
        static void UiWindowEquipment_OnClose_Postfix()
        {
            if (sizeControlPanel != null)
            {
                // Controlla se il pannello è figlio di questa specifica finestra
                // (anche se con un solo pannello globale questo è meno critico)
                var windowsHandler = Managers.GetManager<WindowsHandler>();
                var eqWindow = windowsHandler.GetWindowViaUiId(DataConfig.UiType.Equipment);
                if (eqWindow != null && sizeControlPanel.transform.IsChildOf(eqWindow.transform))
                {
                    logger.LogInfo("UiWindowEquipment.OnClose: Destroying size control panel associated with this window.");
                    Destroy(sizeControlPanel);
                    sizeControlPanel = null;
                }
                else if (sizeControlPanel != null)
                {
                    // Se esiste ma non è figlio, potrebbe essere un rimasuglio o gestito altrove.
                    // Per sicurezza, se chiudiamo la finestra equip, e il nostro pannello è attivo, lo chiudiamo.
                    logger.LogInfo("UiWindowEquipment.OnClose: Destroying (potentially orphaned) size control panel.");
                    Destroy(sizeControlPanel);
                    sizeControlPanel = null;
                }
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(UiWindowEquipment), "OnOpen")]
        static void UiWindowEquipment_OnOpen_Postfix(UiWindowEquipment __instance)
        {
            // Verifica se la finestra è effettivamente quella dell'equipaggiamento del giocatore
            // e non, per esempio, un'altra istanza o tipo se ci fossero sottoclassi.
            // In questo caso, il tipo __instance è già UiWindowEquipment.
            logger.LogInfo("UiWindowEquipment.OnOpen_Postfix: Window opened. Requesting creation/update of size control panel.");
            CreateSizeControls(); // Crea o aggiorna il pannello
        }

        static bool IsPlayerInventory(Inventory inventory)
        {
            var playerInventory = GetPlayerInventory();
            return playerInventory != null && inventory == playerInventory;
        }

        static void CreateSizeControls()
        {
            logger.LogInfo("--- CreateSizeControls START ---"); // LOG INIZIO METODO

            var windowsHandler = Managers.GetManager<WindowsHandler>();
            var window = windowsHandler.GetWindowViaUiId(DataConfig.UiType.Equipment);

            if (window == null)
            {
                logger.LogWarning("CreateSizeControls: Equipment window (UI type Equipment) is NULL. Cannot create panel.");
                logger.LogInfo("--- CreateSizeControls END (Window Null) ---");
                return;
            }
            logger.LogInfo($"CreateSizeControls: Equipment window found: Name='{window.name}', ActiveInHierarchy='{window.gameObject.activeInHierarchy}'");

            // Distruggi il pannello vecchio se esiste, per evitare duplicati
            if (sizeControlPanel != null)
            {
                logger.LogInfo($"CreateSizeControls: Existing sizeControlPanel found ('{sizeControlPanel.name}'). Destroying it before creating a new one.");
                Destroy(sizeControlPanel); // Destroy è asincrono, l'oggetto sarà rimosso alla fine del frame
                sizeControlPanel = null;   // Imposta subito a null
            }

            Transform inventoryContainerParentTransform = null; // Questo sarà il genitore del nostro sizeControlPanel
            UiWindowEquipment equipmentWindowInstance = window as UiWindowEquipment;

            if (equipmentWindowInstance != null && equipmentWindowInstance.playerInventoryContainer != null)
            {
                // playerInventoryContainer è il GameObject che contiene la griglia dell'inventario.
                // Vogliamo mettere il nostro pannello come fratello di playerInventoryContainer,
                // quindi il genitore del nostro pannello sarà il genitore di playerInventoryContainer.
                if (equipmentWindowInstance.playerInventoryContainer.transform.parent != null)
                {
                    inventoryContainerParentTransform = equipmentWindowInstance.playerInventoryContainer.transform.parent;
                    logger.LogInfo($"CreateSizeControls: Target parent for panel is parent of 'playerInventoryContainer': Name='{inventoryContainerParentTransform.name}'");
                }
                else
                {
                    // Fallback: se playerInventoryContainer non ha un genitore (improbabile se è in una UI),
                    // usa la finestra stessa come genitore. Potrebbe non essere ideale per il layout.
                    inventoryContainerParentTransform = window.transform;
                    logger.LogWarning($"CreateSizeControls: 'playerInventoryContainer' has no parent. Using window '{window.name}' as parent for panel.");
                }
            }
            else
            {
                logger.LogWarning($"CreateSizeControls: UiWindowEquipment cast failed or equipmentWindowInstance.playerInventoryContainer is null. Trying to find parent via window '{window.name}' itself.");
                // Se non possiamo accedere a playerInventoryContainer direttamente,
                // usiamo la finestra come genitore. Questo è un fallback meno preciso.
                inventoryContainerParentTransform = window.transform;
            }

            if (inventoryContainerParentTransform == null)
            {
                logger.LogError("CreateSizeControls: CRITICAL - Could not determine a valid parent transform for the size control panel. Aborting panel creation.");
                logger.LogInfo("--- CreateSizeControls END (Parent Null) ---");
                return;
            }

            // Crea il pannello di controllo
            sizeControlPanel = new GameObject("MyCustom_SizeControlPanel"); // Nome univoco per trovarlo con UnityExplorer
            logger.LogInfo($"CreateSizeControls: Created GameObject '{sizeControlPanel.name}'.");
            sizeControlPanel.transform.SetParent(inventoryContainerParentTransform, false); // false per worldPositionStays
            logger.LogInfo($"CreateSizeControls: Set parent of '{sizeControlPanel.name}' to '{inventoryContainerParentTransform.name}'. Current localScale: {sizeControlPanel.transform.localScale}");


            RectTransform panelRect = sizeControlPanel.AddComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(1f, 0f); // Basso-destra genitore
            panelRect.anchorMax = new Vector2(1f, 0f); // Basso-destra genitore
            panelRect.pivot = new Vector2(1f, 0f);     // Basso-destra pannello

            // Spostato di 100px a sinistra dal bordo destro, e 100px in alto dal bordo inferiore
            panelRect.anchoredPosition = new Vector2(-100, 100);
            panelRect.sizeDelta = new Vector2(350, 50);
            logger.LogInfo($"CreateSizeControls: PanelRect: AnchorMin={panelRect.anchorMin}, AnchorMax={panelRect.anchorMax}, Pivot={panelRect.pivot}, AnchoredPosition={panelRect.anchoredPosition}, SizeDelta={panelRect.sizeDelta}");


            // Background
            Image bg = sizeControlPanel.AddComponent<Image>();
            bg.color = new Color(0.1f, 0.1f, 0.1f, 0.85f); // Sfondo scuro
            bg.raycastTarget = true; // Importante per l'interazione UI se ci fossero elementi sotto che non vuoi cliccare attraverso

            // Label
            GameObject labelGO = new GameObject("PanelLabel");
            labelGO.transform.SetParent(sizeControlPanel.transform, false);
            RectTransform labelRect = labelGO.AddComponent<RectTransform>();
            Text labelText = labelGO.AddComponent<Text>();
            labelText.text = "Backpack Size:";
            labelText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            labelText.fontSize = 18; // Più grande
            labelText.color = Color.white;
            labelText.alignment = TextAnchor.MiddleLeft;
            // Configura RectTransform per Label
            labelRect.anchorMin = new Vector2(0, 0.5f);
            labelRect.anchorMax = new Vector2(0, 0.5f);
            labelRect.pivot = new Vector2(0, 0.5f);
            labelRect.anchoredPosition = new Vector2(10, 0); // 10px dal bordo sinistro, centrato verticalmente
            labelRect.sizeDelta = new Vector2(120, 30); // Larghezza per il testo

            // Input Field
            GameObject inputGO = new GameObject("PanelInputField");
            inputGO.transform.SetParent(sizeControlPanel.transform, false);
            RectTransform inputRect = inputGO.AddComponent<RectTransform>();
            Image inputBg = inputGO.AddComponent<Image>();
            inputBg.color = new Color(0.2f, 0.2f, 0.2f, 1f);
            InputField inputField = inputGO.AddComponent<InputField>();
            // Configura RectTransform per InputField
            inputRect.anchorMin = new Vector2(0.5f, 0.5f); // Centro (relativo)
            inputRect.anchorMax = new Vector2(0.5f, 0.5f);
            inputRect.pivot = new Vector2(0.5f, 0.5f);
            inputRect.anchoredPosition = new Vector2(-30, 0); // Spostato un po' a sinistra dal centro del pannello
            inputRect.sizeDelta = new Vector2(80, 30); // Dimensione per l'input

            GameObject inputTextGO = new GameObject("PanelInputText");
            inputTextGO.transform.SetParent(inputGO.transform, false);
            RectTransform textRect = inputTextGO.AddComponent<RectTransform>();
            Text textComponent = inputTextGO.AddComponent<Text>();
            textComponent.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            textComponent.fontSize = 16;
            textComponent.color = Color.white;
            textComponent.alignment = TextAnchor.MiddleCenter;
            textComponent.supportRichText = false;
            // Configura RectTransform per il testo dell'InputField (riempie l'InputField)
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(5, 0); // Padding interno
            textRect.offsetMax = new Vector2(-5, 0);

            inputField.targetGraphic = inputBg;
            inputField.textComponent = textComponent;
            inputField.contentType = InputField.ContentType.IntegerNumber;

            var playerInventory = GetPlayerInventory();
            if (playerInventory != null)
            {
                inputField.text = playerInventory.GetSize().ToString();
                logger.LogInfo($"CreateSizeControls: InputField text set to '{inputField.text}'.");
            }

            // Button
            GameObject buttonGO = new GameObject("PanelSetButton");
            buttonGO.transform.SetParent(sizeControlPanel.transform, false);
            RectTransform buttonRect = buttonGO.AddComponent<RectTransform>();
            Image buttonImg = buttonGO.AddComponent<Image>();
            buttonImg.color = new Color(0.2f, 0.5f, 0.2f, 1f); // Verde più scuro
            Button button = buttonGO.AddComponent<Button>();
            button.targetGraphic = buttonImg;
            // Configura RectTransform per Button
            buttonRect.anchorMin = new Vector2(1, 0.5f); // Bordo destro, centro verticale
            buttonRect.anchorMax = new Vector2(1, 0.5f);
            buttonRect.pivot = new Vector2(1, 0.5f);
            buttonRect.anchoredPosition = new Vector2(-10, 0); // 10px dal bordo destro
            buttonRect.sizeDelta = new Vector2(100, 30);

            GameObject buttonTextGO = new GameObject("PanelButtonText");
            buttonTextGO.transform.SetParent(buttonGO.transform, false);
            RectTransform btnTextRect = buttonTextGO.AddComponent<RectTransform>();
            Text btnText = buttonTextGO.AddComponent<Text>();
            btnText.text = "Set Size";
            btnText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            btnText.fontSize = 16;
            btnText.color = Color.white;
            btnText.alignment = TextAnchor.MiddleCenter;
            // Configura RectTransform per il testo del bottone (riempie il bottone)
            btnTextRect.anchorMin = Vector2.zero;
            btnTextRect.anchorMax = Vector2.one;
            btnTextRect.offsetMin = Vector2.zero;
            btnTextRect.offsetMax = Vector2.zero;

            // Click handler
            button.onClick.AddListener(() =>
            {
                logger.LogInfo($"Set Size Button Clicked. InputField text: '{inputField.text}'"); // LOG QUANDO PREMUTO
                if (int.TryParse(inputField.text, out int newSize))
                {
                    logger.LogInfo($"Parsed newSize: {newSize}");
                    // Aggiungi limiti ragionevoli, es. 1-500 o quello che il gioco gestisce bene
                    if (newSize > 0 && newSize <= 1000) // Puoi regolare 1000 se necessario
                    {
                        var inv = GetPlayerInventory();
                        if (inv != null)
                        {
                            logger.LogInfo($"Attempting to set inventory (ID: {inv.GetId()}) size from {inv.GetSize()} to {newSize}");
                            SetInventorySize(inv, newSize);
                            // Non c'è bisogno di loggare qui "Setting inventory size to {newSize}"
                            // perché SetInventorySize già lo fa.
                        }
                        else
                        {
                            logger.LogWarning("Player inventory is null. Cannot set size.");
                        }
                    }
                    else
                    {
                        logger.LogWarning($"Invalid size: {newSize}. Must be between 1 and 1000 (adjust as needed).");
                        // Opzionale: reimposta l'input field al valore corrente se l'input è errato
                        var currentInv = GetPlayerInventory();
                        if (currentInv != null)
                        {
                            inputField.text = currentInv.GetSize().ToString();
                        }
                    }
                }
                else
                {
                    logger.LogWarning($"Failed to parse '{inputField.text}' as an integer.");
                    // Opzionale: reimposta l'input field al valore corrente se l'input non è un numero
                    var currentInv = GetPlayerInventory();
                    if (currentInv != null)
                    {
                        inputField.text = currentInv.GetSize().ToString();
                    }
                }
            });

            logger.LogInfo("CreateSizeControls: Button OnClick listener added.");

            sizeControlPanel.SetActive(showSizeControlPanelConfig);
            logger.LogInfo($"CreateSizeControls: Panel '{sizeControlPanel.name}' initial activeSelf: {sizeControlPanel.activeSelf}, based on showSizeControlPanelConfig: {showSizeControlPanelConfig}");

            logger.LogInfo($"CreateSizeControls: Panel '{sizeControlPanel.name}' final state: activeSelf='{sizeControlPanel.activeSelf}', activeInHierarchy='{sizeControlPanel.activeInHierarchy}'. Layer: {LayerMask.LayerToName(sizeControlPanel.layer)}");
            logger.LogInfo("--- CreateSizeControls END (Success) ---");
        }

        static Inventory GetPlayerInventory()
        {
            var pm = Managers.GetManager<PlayersManager>();
            if (pm != null)
            {
                var player = pm.GetActivePlayerController();
                if (player != null)
                {
                    return player.GetPlayerBackpack()?.GetInventory();
                }
            }
            return null;
        }

        // Patch per auto-set size quando spawni
        [HarmonyPostfix]
        [HarmonyPatch(typeof(PlayerMainController), "OnNetworkSpawn")]
        static void PlayerMainController_OnNetworkSpawn(PlayerMainController __instance)
        {
            if (__instance.IsOwner && autoSetOnSpawn.Value)
            {
                __instance.StartCoroutine(SetBackpackSizeDelayed(__instance));
            }
        }

        static IEnumerator SetBackpackSizeDelayed(PlayerMainController player)
        {
            yield return new WaitForSeconds(2f);

            var inventory = player.GetPlayerBackpack()?.GetInventory();
            if (inventory != null)
            {
                SetInventorySize(inventory, defaultBackpackSize.Value);
            }
        }

        // Il tuo metodo per settare la size
        public static void SetInventorySize(Inventory inventory, int targetSize)
        {
            if (inventory == null) return;

            var currentSize = inventory.GetSize();
            if (currentSize == targetSize)
            {
                logger.LogInfo($"SetInventorySize: Target size {targetSize} is already the current size. No change needed.");
                return;
            }
            var offset = targetSize - currentSize;

            logger.LogInfo($"Setting inventory size: {currentSize} -> {targetSize} (offset: {offset})");

            InventoriesHandler.Instance.SetInventorySize(
                inventory,
                offset,
                Vector3.zero,
                (success) =>
                {
                    logger.LogInfo($"SetInventorySize success: {success}");
                    if (success)
                    {
                        logger.LogInfo($"New size: {inventory.GetSize()}");
                    }
                }
            );
        }

        // Patch per fixare la viewport (preso dalla mod EquipmentExtend)
        [HarmonyPostfix]
        [HarmonyPatch(typeof(InventoryDisplayer), "TrueRefreshContent")]
        public static void TrueRefreshContent(InventoryDisplayer __instance, GridLayoutGroup ____grid, Inventory ____inventory)
        {
            var playerInventory = GetPlayerInventory();
            if (____inventory != playerInventory) // Applica solo all'inventario del giocatore
            {
                return;
            }

            // Se l'inventario è piccolo, non fare nulla
            if (____inventory.GetSize() <= 54) return;
            logger.LogInfo($"Large inventory detected: {____inventory.GetSize()} slots. Applying viewport fix.");

            // Logica per aggiungere scrollbar quando serve
            if (____grid.transform.parent != null && ____grid.transform.parent.name == "MyCustom_ViewPort")
            {
                // La griglia è già dentro il nostro viewport customizzato, quindi aggiorniamo
                UpdateScrollView(__instance, ____grid, ____inventory);
            }
            else
            {
                // La griglia non è nel nostro viewport, quindi creiamo la struttura ScrollView
                CreateScrollView(__instance, ____grid, ____inventory);
            }
        }

        static void CreateScrollView(InventoryDisplayer __instance, GridLayoutGroup grid, Inventory inventory)
        {
            int columns = 6; // Mantieni 6 colonne
            int rows = (int)Math.Ceiling((double)inventory.GetSize() / columns);

            float cellWidth = grid.cellSize.x;
            float cellHeight = grid.cellSize.y;
            float spacingX = grid.spacing.x;
            float spacingY = grid.spacing.y;

            float contentWidth = columns * (cellWidth + spacingX);
            float contentHeight = rows * (cellHeight + spacingY);

            // Limita l'altezza visibile
            float maxVisibleHeight = 400f; // Puoi aggiustare questo valore
            float visibleHeight = Math.Min(contentHeight, maxVisibleHeight);
            logger.LogInfo($"CreateScrollView: Creating scroll view for inventory with {inventory.GetSize()} slots.");
            // Crea ScrollView
            GameObject scrollViewGO = new GameObject("MyCustom_ScrollView"); // Nome univoco
            scrollViewGO.transform.SetParent(grid.transform.parent, false); // false per worldPositionStays
            RectTransform scrollViewRect = scrollViewGO.AddComponent<RectTransform>(); // Rinominato per chiarezza
            // scrollViewRect dovrebbe riempire l'area dove era prima la griglia, o un'area definita.
            // Se grid.transform.parent era il contenitore originale, facciamo così:
            scrollViewRect.anchorMin = Vector2.zero;
            scrollViewRect.anchorMax = Vector2.one;
            scrollViewRect.sizeDelta = Vector2.zero; // Riempie il genitore
            scrollViewRect.anchoredPosition = Vector2.zero;
            // Oppure, se vuoi dimensioni fisse/calcolate per lo ScrollView:
            // scrollViewRect.sizeDelta = new Vector2(contentWidth + 20f, visibleHeight); // +20f per scrollbar se a destra
            // scrollViewRect.anchoredPosition = new Vector2(0f, -50f); // Posizionamento ScrollView

            logger.LogInfo($"CreateScrollView: ScrollView '{scrollViewGO.name}' created. Parent: '{scrollViewGO.transform.parent.name}'. AnchoredPos: {scrollViewRect.anchoredPosition}, SizeDelta: {scrollViewRect.sizeDelta}");


            // Viewport
            GameObject viewportGO = new GameObject("MyCustom_ViewPort"); // Nome univoco
            viewportGO.transform.SetParent(scrollViewGO.transform, false);
            RectTransform viewportRect = viewportGO.AddComponent<RectTransform>();
            // La viewport di solito riempie lo ScrollView, tranne lo spazio per le scrollbar se non sono overlay.
            viewportRect.anchorMin = Vector2.zero;
            viewportRect.anchorMax = Vector2.one;
            // Se la scrollbar è a sinistra e larga 20px, e vogliamo un padding di 5px:
            viewportRect.offsetMin = new Vector2(25, 0); // Spazio a sinistra per scrollbar (20px) + padding (5px)
            viewportRect.offsetMax = new Vector2(0, 0);  // Nessun offset a destra o in alto/basso
            viewportRect.pivot = new Vector2(0, 1); // Pivot alto-sinistra per la viewport
            logger.LogInfo($"CreateScrollView: Viewport '{viewportGO.name}' created. OffsetMin: {viewportRect.offsetMin}, OffsetMax: {viewportRect.offsetMax}");


            Image viewportImage = viewportGO.AddComponent<Image>();
            viewportImage.color = new Color(0, 0, 0, 0.01f); // Quasi trasparente
            viewportGO.AddComponent<Mask>().showMaskGraphic = false;

            // Sposta la grid nel viewport
            grid.transform.SetParent(viewportGO.transform, false);
            RectTransform gridRect = grid.GetComponent<RectTransform>();
            gridRect.anchorMin = new Vector2(0, 1); // Alto-sinistra dell'area contenuto
            gridRect.anchorMax = new Vector2(1, 1); // Alto-destra dell'area contenuto (si estende orizzontalmente)
            gridRect.pivot = new Vector2(0.5f, 1f); // Pivot al centro-alto della griglia

            // Padding superiore per la griglia quando è in cima
            float topPadding = 20f; // Spazio di 20px in alto
            gridRect.anchoredPosition = new Vector2(0, -topPadding); // Sposta la griglia 20px verso il basso
            // La dimensione della griglia deve tenere conto del padding se vuoi che il contenuto scrollabile inizi effettivamente più in basso.
            // Se il padding è solo visivo e non deve influenzare l'altezza totale dello scroll:
            gridRect.sizeDelta = new Vector2(0, contentHeight); // Larghezza 0 (si estende con anchor), altezza calcolata
            // Se il padding DEVE aumentare l'area scrollabile:
            // gridRect.sizeDelta = new Vector2(0, contentHeight + topPadding);

            logger.LogInfo($"CreateScrollView: Grid '{grid.name}' reparented. AnchoredPos: {gridRect.anchoredPosition}, SizeDelta: {gridRect.sizeDelta}");


            // Aggiungi ScrollRect allo ScrollView GameObject
            ScrollRect scrollRectComponent = scrollViewGO.AddComponent<ScrollRect>();
            scrollRectComponent.content = gridRect;      // La griglia è il contenuto che scorre
            scrollRectComponent.viewport = viewportRect; // La viewport è la finestra di visualizzazione
            scrollRectComponent.horizontal = false;
            scrollRectComponent.vertical = true;
            scrollRectComponent.scrollSensitivity = 30f;
            scrollRectComponent.movementType = ScrollRect.MovementType.Clamped; // O Elastic se preferisci

            // Aggiungi scrollbar (la funzione AddScrollbar ora la posiziona a sinistra)
            if (contentHeight > viewportRect.rect.height) // Controlla se il contenuto è più alto della viewport effettiva
            {
                AddScrollbar(scrollViewGO, scrollRectComponent);
            }
            else
            {
                // Se non serve la scrollbar, assicurati che non ce ne sia una vecchia
                var existingScrollbar = scrollViewGO.GetComponentInChildren<Scrollbar>();
                if (existingScrollbar != null) Destroy(existingScrollbar.gameObject);
                scrollRectComponent.verticalScrollbar = null; // Rimuovi riferimento
                logger.LogInfo("CreateScrollView: Content fits viewport, no scrollbar needed or old one removed.");
            }
            logger.LogInfo("--- CreateScrollView END ---");
        }

        static void UpdateScrollView(InventoryDisplayer __instance, GridLayoutGroup grid, Inventory inventory)
        {
            logger.LogInfo($"UpdateScrollView: Updating scroll view for inventory with {inventory.GetSize()} slots.");
            RectTransform gridRect = grid.GetComponent<RectTransform>();
            RectTransform viewportRect = grid.transform.parent.GetComponent<RectTransform>(); // Assumendo che il genitore sia la viewport
            ScrollRect scrollRectComponent = grid.transform.parent.parent.GetComponent<ScrollRect>(); // Assumendo che il nonno sia lo ScrollView

            if (gridRect == null || viewportRect == null || scrollRectComponent == null)
            {
                logger.LogWarning("UpdateScrollView: Could not find all necessary RectTransforms (grid, viewport, scrollRect). Aborting update.");
                return;
            }

            int columns = 6; // Dovrebbe essere consistente con CreateScrollView o letto dalla griglia
            int rows = (int)Math.Ceiling((double)inventory.GetSize() / columns);

            float cellHeight = grid.cellSize.y;
            float spacingY = grid.spacing.y;
            float contentHeight = rows * (cellHeight + spacingY) + grid.padding.top + grid.padding.bottom; // Include padding della griglia

            // float topPadding = 20f; // Lo stesso valore usato in CreateScrollView se influenza l'altezza totale
            // Se il topPadding è solo un offset, non aggiungerlo qui a contentHeight.
            // Ma l'anchoredPosition della griglia deve essere rispettata.
            // gridRect.anchoredPosition = new Vector2(gridRect.anchoredPosition.x, -topPadding);

            gridRect.sizeDelta = new Vector2(gridRect.sizeDelta.x, contentHeight); // Aggiorna solo l'altezza

            // Controlla se la scrollbar è ancora necessaria
            var existingScrollbar = scrollRectComponent.verticalScrollbar;
            if (contentHeight > viewportRect.rect.height)
            {
                if (existingScrollbar == null)
                {
                    logger.LogInfo("UpdateScrollView: Content now exceeds viewport, adding scrollbar.");
                    AddScrollbar(scrollRectComponent.gameObject, scrollRectComponent); // Passa ScrollView GO e ScrollRect
                }
                else if (!existingScrollbar.gameObject.activeSelf)
                {
                    existingScrollbar.gameObject.SetActive(true); // Riattiva se era nascosta
                }
            }
            else
            {
                if (existingScrollbar != null && existingScrollbar.gameObject.activeSelf)
                {
                    logger.LogInfo("UpdateScrollView: Content now fits viewport, hiding or removing scrollbar.");
                    // Potresti volerla solo nascondere se usi AutoHide
                    // existingScrollbar.gameObject.SetActive(false);
                    // Oppure rimuoverla e il riferimento se non vuoi che AutoHide la gestisca
                    Destroy(existingScrollbar.gameObject);
                    scrollRectComponent.verticalScrollbar = null;
                }
            }
            logger.LogInfo($"UpdateScrollView: Grid height updated to {contentHeight}. Viewport height: {viewportRect.rect.height}");
        }


        static void AddScrollbar(GameObject scrollViewGO, ScrollRect scrollRectInstance) // Parametri rinominati per chiarezza
        {
            GameObject scrollbarGO = new GameObject("VerticalScrollbarLeft");
            scrollbarGO.transform.SetParent(scrollViewGO.transform, false);

            RectTransform scrollbarRect = scrollbarGO.AddComponent<RectTransform>();
            scrollbarRect.anchorMin = new Vector2(0, 0);
            scrollbarRect.anchorMax = new Vector2(0, 1);
            scrollbarRect.pivot = new Vector2(0, 0.5f);
            scrollbarRect.sizeDelta = new Vector2(20, 0);
            scrollbarRect.anchoredPosition = new Vector2(10, 0); // 10px dal bordo sinistro

            Image scrollbarBg = scrollbarGO.AddComponent<Image>();
            scrollbarBg.color = new Color(0.1f, 0.1f, 0.1f, 0.5f);

            Scrollbar scrollbarComponent = scrollbarGO.AddComponent<Scrollbar>(); // Rinominato per chiarezza
            scrollbarComponent.direction = Scrollbar.Direction.BottomToTop;

            GameObject handleGO = new GameObject("Handle");
            handleGO.transform.SetParent(scrollbarGO.transform, false);
            RectTransform handleRect = handleGO.AddComponent<RectTransform>();
            handleRect.sizeDelta = new Vector2(0, 20);

            Image handleImg = handleGO.AddComponent<Image>();
            handleImg.color = new Color(0.8f, 0.8f, 0.8f, 0.7f);

            scrollbarComponent.targetGraphic = handleImg;
            scrollbarComponent.handleRect = handleRect;

            // USA IL NOME DEL PARAMETRO CORRETTO QUI:
            scrollRectInstance.verticalScrollbar = scrollbarComponent;
            scrollRectInstance.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHideAndExpandViewport;
            scrollRectInstance.verticalScrollbarSpacing = 5; // Prova con un valore positivo piccolo, lo spazio viene creato TRA la viewport e la scrollbar
                                                             // Se la scrollbar è a sinistra, e la viewport è stata ristretta a sinistra (con offsetMin),
                                                             // questo potrebbe non essere necessario o potrebbe essere 0.
                                                             // Se AutoHideAndExpandViewport è usato, la viewport dovrebbe restringersi per fare spazio.
                                                             // Dato che hai impostato viewportRect.offsetMin = new Vector2(25, 0);
                                                             // hai già fatto spazio. Prova con 0 o un valore molto piccolo per lo spacing.
                                                             // O anche un valore negativo se vuoi che la scrollbar si sovrapponga leggermente al padding.
                                                             // Il valore originale -3 era per quando la scrollbar è DENTRO la viewport. Ora è un fratello.
                                                             // Prova con 0 o 5.

            logger.LogInfo("AddScrollbar: Scrollbar created on the left.");
        }

    }
}
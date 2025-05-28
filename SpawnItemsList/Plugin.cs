// ItemSpawnerUI/Plugin.cs
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

namespace ItemSpawnerUI
{
    [BepInPlugin(PLUGIN_GUID, PLUGIN_NAME, PLUGIN_VERSION)]
    public class Plugin : BaseUnityPlugin
    {
        public const string PLUGIN_GUID = "Vorshim92.ThePlanetCrafter.ItemSpawnerUI";
        public const string PLUGIN_NAME = "Item Spawner UI";
        public const string PLUGIN_VERSION = "1.0.0";

        private static ManualLogSource logger;
        public static Plugin instance;

        // UI Elements
        private static GameObject mainPanel;
        private static bool showMainPanel = false;

        // Config
        private static ConfigEntry<KeyboardShortcut> togglePanelShortcut;
        private static ConfigEntry<bool> showPanelOnInventoryOpen;

        // Spawner state
        private static SpaceCraft.Group selectedGroupToSpawn = null;
        private static InputField quantityInputField;
        private static Text selectedItemText;
        private static ScrollRect itemsScrollRect;
        private static Transform itemsContentTransform;
        
        // Cached data
        private static List<CachedGroupData> cachedGroups = new List<CachedGroupData>();
        private static bool groupsCached = false;
        private static Font gameFont = null;

        // Struttura per cachare i dati dei gruppi
        private class CachedGroupData
        {
            public SpaceCraft.Group group;
            public string id;
            public string displayName;
            
            public CachedGroupData(SpaceCraft.Group g)
            {
                group = g;
                id = g.id;
                displayName = SpaceCraft.Readable.GetGroupName(g);
                // Se il display name è uguale all'ID, significa che non ha un nome localizzato
                if (string.IsNullOrEmpty(displayName) || displayName == id)
                {
                    displayName = id; // Usa l'ID come fallback
                }
            }
        }

        private void Awake()
        {
            instance = this;
            logger = Logger;

            // Configurazione
            togglePanelShortcut = Config.Bind("General", "TogglePanelShortcut", new KeyboardShortcut(KeyCode.I, KeyCode.LeftControl), "Shortcut to toggle the Item Spawner panel.");
            showPanelOnInventoryOpen = Config.Bind("General", "ShowPanelOnInventoryOpen", true, "Automatically show the spawner panel when the player's inventory (backpack) UI is opened.");

            Harmony.CreateAndPatchAll(typeof(Plugin));
            logger.LogInfo($"{PLUGIN_NAME} loaded!");
            
            // Prova a cachare i gruppi dopo un breve delay per assicurarsi che il gioco sia inizializzato
            StartCoroutine(CacheGroupsDelayed());
        }

        private IEnumerator CacheGroupsDelayed()
        {
            yield return new WaitForSeconds(5f); // Aspetta 5 secondi dopo l'avvio
            CacheAllGroups();
        }

        private static void CacheAllGroups()
        {
            if (groupsCached) return;
            
            try
            {
                logger.LogInfo("Starting to cache all groups...");
                
                var allGroups = SpaceCraft.GroupsHandler.GetAllGroups();
                if (allGroups == null || allGroups.Count == 0)
                {
                    logger.LogWarning("No groups found to cache!");
                    return;
                }
                
                cachedGroups.Clear();
                
                foreach (var group in allGroups)
                {
                    if (group == null || string.IsNullOrEmpty(group.id)) continue;
                    
                    // Includi sia GroupItem che GroupConstructible (tutto ciò che si può spawnare)
                    // Escludi solo cose che sicuramente non ha senso spawnare
                    cachedGroups.Add(new CachedGroupData(group));
                }
                
                // Ordina per nome display
                cachedGroups = cachedGroups.OrderBy(g => g.displayName).ToList();
                
                groupsCached = true;
                logger.LogInfo($"Successfully cached {cachedGroups.Count} spawnable items.");
            }
            catch (Exception ex)
            {
                logger.LogError($"Error caching groups: {ex}");
            }
        }

        private static Font GetGameFont()
        {
            if (gameFont != null) return gameFont;
            
            // Prova a trovare un font dal gioco stesso
            var allFonts = Resources.FindObjectsOfTypeAll<Font>();
            if (allFonts != null && allFonts.Length > 0)
            {
                // Cerca font specifici del gioco
                gameFont = allFonts.FirstOrDefault(f => f.name.Contains("Roboto-Regular")) 
                          ?? allFonts.FirstOrDefault(f => f.name.Contains("Roboto"))
                          ?? allFonts.FirstOrDefault(f => f.name.Contains("Legacy")) 
                          ?? allFonts[0];
                          
                logger.LogInfo($"Using font: {gameFont.name}");
            }
            else
            {
                // Fallback ad Arial
                gameFont = Font.CreateDynamicFontFromOSFont("Arial", 14);
                if (gameFont == null)
                {
                    gameFont = Resources.GetBuiltinResource<Font>("Arial.ttf");
                }
                logger.LogWarning("No game fonts found, using Arial fallback");
            }
            
            return gameFont;
        }

        private void Update()
        {
            if (togglePanelShortcut.Value.IsDown())
            {
                // Se i gruppi non sono ancora stati cachati, fallo ora
                if (!groupsCached)
                {
                    CacheAllGroups();
                }
                
                if (mainPanel == null)
                {
                    logger.LogWarning("Toggle shortcut pressed, but ItemSpawner panel has not been initialized yet.");
                    return;
                }

                showMainPanel = !showMainPanel;
                mainPanel.SetActive(showMainPanel);
                logger.LogInfo($"Item Spawner panel toggled via shortcut. Panel active: {mainPanel.activeSelf}");

                if (showMainPanel && itemsContentTransform != null && itemsContentTransform.childCount == 0)
                {
                    PopulateItemList();
                }
            }
        }

        private void OnDestroy()
        {
            if (mainPanel != null) Destroy(mainPanel);
        }

        // --- Harmony Patches ---
        [HarmonyPostfix]
        [HarmonyPatch(typeof(UiWindowEquipment), "OnOpen")]
        static void OnPlayerInventoryOpened(UiWindowEquipment __instance)
        {
            logger.LogInfo($"Player inventory UI opened.");
            
            // Assicurati che i gruppi siano cachati
            if (!groupsCached)
            {
                CacheAllGroups();
            }
            
            if (showPanelOnInventoryOpen.Value)
            {
                showMainPanel = true;
                Transform canvasTransform = __instance.transform.GetComponentInParent<Canvas>()?.transform;

                if (canvasTransform != null)
                {
                    logger.LogInfo($"Found Canvas parent: {canvasTransform.name}");
                    CreateOrUpdateMainPanel(canvasTransform);
                }
                else
                {
                    logger.LogWarning($"Could not find Canvas parent. Using direct parent.");
                    CreateOrUpdateMainPanel(__instance.transform.parent);
                }
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(UiWindowEquipment), "OnClose")]
        static void OnPlayerInventoryClosed()
        {
            logger.LogInfo("Player inventory UI closed.");
            if (mainPanel != null && showPanelOnInventoryOpen.Value)
            {
                showMainPanel = false;
                mainPanel.SetActive(false);
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(WindowsHandler), "CloseAllWindows")]
        static void OnCloseAllWindows()
        {
            if (mainPanel != null)
            {
                logger.LogInfo("CloseAllWindows called, hiding spawner panel.");
                showMainPanel = false;
                mainPanel.SetActive(false);
            }
        }

        // --- UI Creation ---
        static void CreateOrUpdateMainPanel(Transform parentWindowTransform)
        {
            if (parentWindowTransform == null)
            {
                logger.LogError("CreateOrUpdateMainPanel: parentWindowTransform is null!");
                return;
            }

            if (mainPanel == null)
            {
                logger.LogInfo("Creating Item Spawner panel...");
                mainPanel = new GameObject("ItemSpawnerUIMainPanel");
                mainPanel.layer = LayerMask.NameToLayer("UI");
                mainPanel.transform.SetParent(parentWindowTransform, false);

                RectTransform panelRect = mainPanel.AddComponent<RectTransform>();
                panelRect.anchorMin = new Vector2(1, 1);
                panelRect.anchorMax = new Vector2(1, 1);
                panelRect.pivot = new Vector2(1, 1);
                panelRect.sizeDelta = new Vector2(300, 500);
                panelRect.anchoredPosition = new Vector2(-10, -10);

                Image bg = mainPanel.AddComponent<Image>();
                bg.color = new Color(0.1f, 0.1f, 0.2f, 0.9f);

                VerticalLayoutGroup vlg = mainPanel.AddComponent<VerticalLayoutGroup>();
                vlg.padding = new RectOffset(10, 10, 10, 10);
                vlg.spacing = 8;
                vlg.childControlHeight = true;  // Cambiato a true per controllare l'altezza
                vlg.childForceExpandHeight = false; // Mantieni false
                vlg.childControlWidth = true;
                vlg.childForceExpandWidth = true;
                vlg.childAlignment = TextAnchor.UpperCenter;

                // 1. Selected item text
                GameObject selectedItemTextGO = CreateUIText("SelectedItemText_Spawner", "No item selected", mainPanel.transform);
                selectedItemText = selectedItemTextGO.GetComponent<Text>();
                selectedItemText.alignment = TextAnchor.MiddleCenter;
                LayoutElement selectedTextLE = selectedItemTextGO.AddComponent<LayoutElement>();
                selectedTextLE.minHeight = 30;
                selectedTextLE.preferredHeight = 30;

                // 2. ScrollView
                CreateItemsScrollView(mainPanel.transform);

                // 3. Bottom controls
                GameObject bottomPanel = new GameObject("BottomControlPanel_Spawner");
                bottomPanel.layer = LayerMask.NameToLayer("UI");
                bottomPanel.transform.SetParent(mainPanel.transform, false);
                
                RectTransform bottomRect = bottomPanel.AddComponent<RectTransform>();
                
                HorizontalLayoutGroup hlg = bottomPanel.AddComponent<HorizontalLayoutGroup>();
                hlg.padding = new RectOffset(0, 0, 0, 0);
                hlg.spacing = 10;
                hlg.childAlignment = TextAnchor.MiddleCenter;
                hlg.childControlHeight = false;
                hlg.childForceExpandHeight = false;
                hlg.childControlWidth = false;
                hlg.childForceExpandWidth = false;

                LayoutElement bottomPanelLE = bottomPanel.AddComponent<LayoutElement>();
                bottomPanelLE.minHeight = 35;
                bottomPanelLE.preferredHeight = 35;
                bottomPanelLE.flexibleHeight = 0; // Non espandere

                // Quantity input
                GameObject inputGO = CreateUIInputField("QuantityInput_Spawner", "1", bottomPanel.transform);
                quantityInputField = inputGO.GetComponent<InputField>();
                quantityInputField.contentType = InputField.ContentType.IntegerNumber;
                RectTransform inputRect = inputGO.GetComponent<RectTransform>();
                inputRect.sizeDelta = new Vector2(80, 30);

                // Spawn button
                GameObject spawnButtonGO = CreateUIButton("SpawnButton_Spawner", "Spawn", bottomPanel.transform, OnSpawnButtonPressed);
                RectTransform buttonRect = spawnButtonGO.GetComponent<RectTransform>();
                buttonRect.sizeDelta = new Vector2(150, 30);

                PopulateItemList();
            }
            
            mainPanel.SetActive(showMainPanel);
        }

        static void CreateItemsScrollView(Transform parent)
        {
            GameObject scrollViewGO = new GameObject("ItemsScrollView_Spawner");
            scrollViewGO.layer = LayerMask.NameToLayer("UI");
            scrollViewGO.transform.SetParent(parent, false);
            
            RectTransform scrollRectTransform = scrollViewGO.GetComponent<RectTransform>();
            
            itemsScrollRect = scrollViewGO.AddComponent<ScrollRect>();
            Image scrollBg = scrollViewGO.AddComponent<Image>();
            scrollBg.color = new Color(0.05f, 0.05f, 0.1f, 0.5f);
            
            // Layout element con flessibilità massima
            LayoutElement scrollLE = scrollViewGO.AddComponent<LayoutElement>();
            scrollLE.flexibleHeight = 1f;
            scrollLE.flexibleWidth = 1f;
            scrollLE.minHeight = 50; // Minimo molto basso

            // Viewport
            GameObject viewportGO = new GameObject("ItemsViewport_Spawner");
            viewportGO.layer = LayerMask.NameToLayer("UI");
            viewportGO.transform.SetParent(scrollViewGO.transform, false);
            
            RectTransform viewportRect = viewportGO.AddComponent<RectTransform>();
            viewportRect.anchorMin = Vector2.zero;
            viewportRect.anchorMax = Vector2.one;
            viewportRect.sizeDelta = Vector2.zero;
            viewportRect.offsetMin = new Vector2(0, 0);
            viewportRect.offsetMax = new Vector2(-25, 0); // Spazio per scrollbar
            
            Image viewportImg = viewportGO.AddComponent<Image>();
            viewportImg.color = new Color(0, 0, 0, 0.01f); // Quasi trasparente ma presente
            Mask viewportMask = viewportGO.AddComponent<Mask>();
            viewportMask.showMaskGraphic = false;

            // Content
            GameObject contentGO = new GameObject("ItemsContent_Spawner");
            contentGO.layer = LayerMask.NameToLayer("UI");
            contentGO.transform.SetParent(viewportGO.transform, false);
            
            RectTransform contentRect = contentGO.AddComponent<RectTransform>();
            contentRect.anchorMin = new Vector2(0, 1);
            contentRect.anchorMax = new Vector2(1, 1);
            contentRect.pivot = new Vector2(0.5f, 1);
            contentRect.anchoredPosition = Vector2.zero;
            contentRect.sizeDelta = new Vector2(0, 0);
            
            itemsContentTransform = contentGO.transform;
            
            VerticalLayoutGroup contentVLG = contentGO.AddComponent<VerticalLayoutGroup>();
            contentVLG.padding = new RectOffset(5, 5, 5, 5);
            contentVLG.spacing = 2;
            contentVLG.childAlignment = TextAnchor.UpperLeft;
            contentVLG.childControlWidth = true;
            contentVLG.childForceExpandWidth = true;
            contentVLG.childControlHeight = false;
            contentVLG.childForceExpandHeight = false;

            ContentSizeFitter contentFitter = contentGO.AddComponent<ContentSizeFitter>();
            contentFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            contentFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

            // Scrollbar
            GameObject scrollbarGO = CreateUIScrollbar("ItemsScrollbar_Spawner", scrollViewGO.transform);
            Scrollbar sb = scrollbarGO.GetComponent<Scrollbar>();
            
            // Modifica la dimensione della maniglia della scrollbar
            if (sb != null && sb.handleRect != null)
            {
                // Imposta una dimensione minima per la maniglia
                LayoutElement handleLE = sb.handleRect.gameObject.AddComponent<LayoutElement>();
                handleLE.minHeight = 20f; // Altezza minima della maniglia
            }

            // Configure ScrollRect
            itemsScrollRect.viewport = viewportRect;
            itemsScrollRect.content = contentRect;
            itemsScrollRect.horizontal = false;
            itemsScrollRect.vertical = true;
            itemsScrollRect.movementType = ScrollRect.MovementType.Clamped;
            itemsScrollRect.scrollSensitivity = 20;
            
            if (sb != null)
            {
                itemsScrollRect.verticalScrollbar = sb;
                itemsScrollRect.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHideAndExpandViewport;
                itemsScrollRect.verticalScrollbarSpacing = -3;
            }
            
            logger.LogInfo("ScrollView created successfully");
        }

        static void PopulateItemList()
        {
            if (itemsContentTransform == null)
            {
                logger.LogWarning("PopulateItemList: itemsContentTransform is null.");
                return;
            }

            // Clear old items
            foreach (Transform child in itemsContentTransform)
            {
                Destroy(child.gameObject);
            }

            if (!groupsCached || cachedGroups.Count == 0)
            {
                CreateUIText("NoItemsText_Spawner", "No items found.", itemsContentTransform);
                return;
            }

            logger.LogInfo($"Populating list with {cachedGroups.Count} cached items");

            int buttonCount = 0;
            
            foreach (var cachedGroup in cachedGroups)
            {
                string buttonLabel = cachedGroup.displayName;
                
                GameObject itemButtonGO = CreateUIButton($"ItemBtn_{cachedGroup.id}", buttonLabel, itemsContentTransform, () =>
                {
                    OnItemSelected(cachedGroup.group);
                });

                // Imposta dimensione fissa per ogni bottone
                RectTransform btnRect = itemButtonGO.GetComponent<RectTransform>();
                btnRect.sizeDelta = new Vector2(0, 30); // Larghezza 0 = si adatta al parent, altezza 30

                // Stile del bottone
                Image btnImg = itemButtonGO.GetComponent<Image>();
                btnImg.color = new Color(0.3f, 0.3f, 0.4f, 0.8f);
                
                // Configurazione testo
                Text btnText = itemButtonGO.GetComponentInChildren<Text>();
                if (btnText != null)
                {
                    btnText.text = buttonLabel; // Riassegna il testo
                    btnText.color = Color.white; // Bianco invece di giallo ora che sappiamo che funziona
                    btnText.fontSize = 14;
                    btnText.font = GetGameFont();
                }

                LayoutElement itemBtnLE = itemButtonGO.GetComponent<LayoutElement>() ?? itemButtonGO.AddComponent<LayoutElement>();
                itemBtnLE.minHeight = 30;
                itemBtnLE.preferredHeight = 30;
                itemBtnLE.flexibleHeight = 0; // Non espandere
                
                buttonCount++;
            }
            
            logger.LogInfo($"Populated item list with {buttonCount} buttons");
            
            // Forza il rebuild del layout e fix scrollbar
            Canvas.ForceUpdateCanvases();
            
            // Fix per la scrollbar - forza il ricalcolo dopo un frame
            instance.StartCoroutine(FixScrollbarAfterFrame());
        }
        
        static IEnumerator FixScrollbarAfterFrame()
        {
            yield return null; // Aspetta un frame
            
            if (itemsScrollRect != null)
            {
                // Log delle dimensioni per debug
                RectTransform scrollRT = itemsScrollRect.GetComponent<RectTransform>();
                RectTransform viewportRT = itemsScrollRect.viewport;
                RectTransform contentRT = itemsScrollRect.content;
                
                logger.LogInfo($"ScrollView height: {scrollRT.rect.height}");
                logger.LogInfo($"Viewport height: {viewportRT.rect.height}");
                logger.LogInfo($"Content height: {contentRT.rect.height}");
                logger.LogInfo($"Number of items: {itemsContentTransform.childCount}");
                
                // Calcola l'altezza totale che dovrebbe avere il content
                float expectedHeight = (itemsContentTransform.childCount * 30) + // 30 per ogni bottone
                                     (itemsContentTransform.childCount - 1) * 2 + // 2 spacing tra bottoni
                                     10; // padding top+bottom
                logger.LogInfo($"Expected content height: {expectedHeight}");
                
                // Forza il ricalcolo
                if (itemsScrollRect.verticalScrollbar != null)
                {
                    itemsScrollRect.enabled = false;
                    itemsScrollRect.enabled = true;
                    itemsScrollRect.verticalNormalizedPosition = 1f;
                }
            }
        }

        static void OnItemSelected(SpaceCraft.Group group)
        {
            selectedGroupToSpawn = group;
            if (selectedItemText != null)
            {
                string displayName = SpaceCraft.Readable.GetGroupName(group);
                selectedItemText.text = $"Selected: {displayName}";
            }
            logger.LogInfo($"Selected item: {group.id}");
        }

        static void OnSpawnButtonPressed()
        {
            if (selectedGroupToSpawn == null)
            {
                logger.LogWarning("Spawn button pressed, but no item is selected.");
                if (selectedItemText != null) selectedItemText.text = "ERROR: Select an item first!";
                return;
            }

            if (quantityInputField == null || !int.TryParse(quantityInputField.text, out int quantity) || quantity <= 0)
            {
                logger.LogWarning($"Invalid quantity. Defaulting to 1.");
                quantity = 1;
                if (quantityInputField != null) quantityInputField.text = "1";
            }

            logger.LogInfo($"Attempting to spawn {quantity} of '{selectedGroupToSpawn.id}'");

            Inventory playerInventory = GetPlayerMainInventory();
            if (playerInventory == null)
            {
                logger.LogError("Cannot spawn item: Player inventory not found.");
                return;
            }

            InventoriesHandler invHandler = InventoriesHandler.Instance;
            if (invHandler == null)
            {
                logger.LogError("Cannot spawn item: InventoriesHandler.Instance is null.");
                return;
            }

            int spawned = 0;
            for (int i = 0; i < quantity; i++)
            {
                invHandler.AddItemToInventory(selectedGroupToSpawn, playerInventory, (success, newItemId) =>
                {
                    if (success)
                    {
                        spawned++;
                        logger.LogInfo($"Successfully spawned item ID {newItemId}");
                    }
                    else
                    {
                        logger.LogWarning($"Failed to spawn item");
                    }
                });
            }
            
            if (selectedItemText != null)
            {
                string displayName = SpaceCraft.Readable.GetGroupName(selectedGroupToSpawn);
                selectedItemText.text = $"Spawned {quantity}x {displayName}!";
            }
        }

        static Inventory GetPlayerMainInventory()
        {
            var player = Managers.GetManager<PlayersManager>()?.GetActivePlayerController();
            return player?.GetPlayerBackpack()?.GetInventory();
        }

        // --- UI Helpers ---
        static GameObject CreateUIText(string name, string text, Transform parent)
        {
            GameObject go = new GameObject(name);
            go.layer = LayerMask.NameToLayer("UI");
            go.transform.SetParent(parent, false);
            
            Text txt = go.AddComponent<Text>();
            txt.font = GetGameFont();
            txt.text = text;
            txt.color = Color.white;
            txt.fontSize = 14;
            txt.alignment = TextAnchor.MiddleCenter;
            
            return go;
        }

        static GameObject CreateUIButton(string name, string buttonText, Transform parent, UnityEngine.Events.UnityAction onClickAction)
        {
            GameObject buttonGO = new GameObject(name);
            buttonGO.layer = LayerMask.NameToLayer("UI");
            buttonGO.transform.SetParent(parent, false);
            
            // Bottone background
            Image buttonImage = buttonGO.AddComponent<Image>();
            buttonImage.color = new Color(0.2f, 0.2f, 0.3f, 0.8f);
            
            Button button = buttonGO.AddComponent<Button>();
            button.targetGraphic = buttonImage;
            button.onClick.AddListener(onClickAction);
            
            // Aggiungi hover effect
            ColorBlock colors = button.colors;
            colors.normalColor = new Color(0.2f, 0.2f, 0.3f, 0.8f);
            colors.highlightedColor = new Color(0.3f, 0.3f, 0.4f, 0.9f);
            colors.pressedColor = new Color(0.15f, 0.15f, 0.25f, 0.9f);
            colors.selectedColor = colors.highlightedColor;
            colors.colorMultiplier = 1f;
            colors.fadeDuration = 0.1f;
            button.colors = colors;
            
            // Testo del bottone
            GameObject textGO = new GameObject(name + "_Text");
            textGO.layer = LayerMask.NameToLayer("UI");
            textGO.transform.SetParent(buttonGO.transform, false);
            
            Text textComponent = textGO.AddComponent<Text>();
            textComponent.font = GetGameFont();
            textComponent.text = buttonText;
            textComponent.color = Color.white;
            textComponent.fontSize = 14;
            textComponent.alignment = TextAnchor.MiddleLeft;
            textComponent.horizontalOverflow = HorizontalWrapMode.Wrap;
            textComponent.verticalOverflow = VerticalWrapMode.Truncate;
            textComponent.raycastTarget = false; // Il testo non deve intercettare i click
            
            RectTransform textRect = textGO.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.sizeDelta = Vector2.zero;
            textRect.offsetMin = new Vector2(8, 2);
            textRect.offsetMax = new Vector2(-8, -2);
            
            return buttonGO;
        }

        static GameObject CreateUIInputField(string name, string defaultText, Transform parent)
        {
            GameObject go = new GameObject(name);
            go.layer = LayerMask.NameToLayer("UI");
            go.transform.SetParent(parent, false);
            
            Image bg = go.AddComponent<Image>();
            bg.color = new Color(0.1f, 0.1f, 0.1f, 1f);
            
            InputField input = go.AddComponent<InputField>();
            input.targetGraphic = bg;
            
            GameObject textGO = new GameObject(name + "_Text");
            textGO.layer = LayerMask.NameToLayer("UI");
            textGO.transform.SetParent(go.transform, false);
            
            RectTransform textRect = textGO.AddComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(5, 2);
            textRect.offsetMax = new Vector2(-5, -2);
            
            Text text = textGO.AddComponent<Text>();
            text.font = GetGameFont();
            text.text = defaultText;
            text.color = Color.white;
            text.fontSize = 14;
            text.alignment = TextAnchor.MiddleCenter;
            text.supportRichText = false;
            
            input.textComponent = text;
            input.text = defaultText;
            
            return go;
        }

        static GameObject CreateUIScrollbar(string name, Transform parent)
        {
            GameObject scrollbarGO = new GameObject(name);
            scrollbarGO.layer = LayerMask.NameToLayer("UI");
            scrollbarGO.transform.SetParent(parent, false);

            Image scrollbarImg = scrollbarGO.AddComponent<Image>();
            scrollbarImg.color = new Color(0.15f, 0.15f, 0.15f, 0.6f);

            Scrollbar sb = scrollbarGO.AddComponent<Scrollbar>();
            sb.direction = Scrollbar.Direction.BottomToTop;

            // Handle (la parte mobile)
            GameObject handleGO = new GameObject("Handle");
            handleGO.layer = LayerMask.NameToLayer("UI");
            handleGO.transform.SetParent(scrollbarGO.transform, false);
            
            RectTransform handleRect = handleGO.AddComponent<RectTransform>();
            handleRect.sizeDelta = new Vector2(-4, -4); // Un po' più piccolo del parent
            handleRect.anchorMin = new Vector2(0, 0);
            handleRect.anchorMax = new Vector2(1, 1);
            handleRect.offsetMin = new Vector2(2, 2);
            handleRect.offsetMax = new Vector2(-2, -2);
            
            Image handleImg = handleGO.AddComponent<Image>();
            handleImg.color = new Color(0.4f, 0.4f, 0.4f, 0.8f);

            sb.handleRect = handleRect;
            sb.targetGraphic = handleImg;
            sb.size = 0.1f; // Dimensione iniziale della maniglia (10% dell'altezza totale)
            sb.numberOfSteps = 0; // Scroll continuo

            // Posizionamento scrollbar
            RectTransform sbRect = scrollbarGO.GetComponent<RectTransform>();
            sbRect.anchorMin = new Vector2(1, 0);
            sbRect.anchorMax = new Vector2(1, 1);
            sbRect.pivot = new Vector2(1, 0.5f);
            sbRect.sizeDelta = new Vector2(20, 0);
            sbRect.anchoredPosition = Vector2.zero;

            return scrollbarGO;
        }
    }
}
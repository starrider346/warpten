using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Items.Interface.Service;
using Items.Model;
using Newtonsoft.Json.Linq;
using StellarStriderLib;
using TMPro;
using UI.Inventory;
using UnityEngine.UI;
using UnityEngine;
using static System.Math;
using static HarmonyLib.AccessTools;
using static UnityEngine.Object;
using HarmonyEngine = HarmonyLib.Harmony;

namespace WarpTen.Runtime
{
    internal static class CorePanel
    {
        private static InventoryGrid chestGrid;
        private static RectTransform originalParent, matrixSlotRect;
        private static int sibling;
        private static Vector3 originalScale;
        private static CanvasGroup stockPresentation;
        private static bool addedPresentation;
        private static float originalAlpha;
        private static bool originalRaycasts, originalInteractable;
        private static Vector2 anchorMin, anchorMax, pivot, size, position;
        private static GameObject window;
        private static TMP_Text matrixDetails, footer;
        private static PartInstance core;
        private static ScrollRect scroll;
        private static Button coreTab, nacelleTab;
        private static Button matrixTab;
        private static GameObject matrixPage;
        private static bool matrixSelected;
        private const float DiagnosticsHeight = 290;
        private const float PanelWidth = 620;
        private const float PanelHeight = 390;
        private const float ContentWidth = 572;
        private const float NacelleWidth = 286;

        private static readonly string[] CoreFields = new[]
        {
            "Status",
            "Warp eligibility",
            "Production",
            "Hydrogen",
            "Plasma output",
            "Plasma demand",
            "Charge time",
            "Matched coil pairs",
            "Optimal coils",
            "Field strength",
            "Required field",
            "Target speed",
            "Measured speed",
            "Layout",
            "Warp entry"
        };

        private static readonly string[] NacelleFields = new[]
        {
            "Coils",
            "Optimal coils",
            "Plasma connection",
            "Delivered",
            "Demand",
            "Charge",
            "Coil chain"
        };

        private static RectTransform inventorySlot;
        private static bool nacelles, restoring;
        private static float nextRefresh;
        private static TMP_FontAsset font;
        private static readonly Dictionary<string, TMP_Text> fieldValues = new();
        private static bool? displayedTab;
        private static string displayedInfusers;
        private static RectTransform inventoryFrame;
        private static readonly Vector3[] chestCorners = new Vector3[4];

        internal static void Install(HarmonyEngine harmony)
        {
            harmony.Patch(Method(typeof(InventoryGrid), "OnEnable"),
                postfix: new HarmonyMethod(typeof(CorePanel), nameof(Open)));
            harmony.Patch(Method(typeof(InventoryGrid), "OnDisable"),
                prefix: new HarmonyMethod(typeof(CorePanel), nameof(Closed)));
            harmony.Patch(Method(typeof(InventoryGrid), "UpdateInventory"),
                postfix: new HarmonyMethod(typeof(CorePanel), nameof(Synchronize)));
            harmony.Patch(Method(typeof(TooltipEnabler), "UpdateTooltip"),
                prefix: new HarmonyMethod(typeof(CorePanel), nameof(MatrixTooltip)));
            Canvas.preWillRenderCanvases += Refresh;
        }

        private static void Open(InventoryGrid __instance, InventoryType ___source,
            IGenericInventoryProvider ____inventoryProvider)
        {
            if (restoring || ___source != InventoryType.Chest)
                return;
            var selected = CoreInventory.ClientCore(____inventoryProvider);
            if (selected == null)
                return;
            if (chestGrid == __instance && core == selected && window != null)
                return;
            Clear();
            core = selected;
            chestGrid = __instance;
            var slots = (InventorySlotUI[])Field(typeof(InventoryGrid), "_slots").GetValue(chestGrid);
            if (slots == null || slots.Length == 0 || slots[0] == null)
            {
                Clear();
                return;
            }

            matrixSlotRect = (RectTransform)slots[0].transform;
            originalScale = matrixSlotRect.localScale;
            originalParent = (RectTransform)matrixSlotRect.parent;
            sibling = matrixSlotRect.GetSiblingIndex();
            anchorMin = matrixSlotRect.anchorMin;
            anchorMax = matrixSlotRect.anchorMax;
            pivot = matrixSlotRect.pivot;
            size = matrixSlotRect.sizeDelta;
            position = matrixSlotRect.anchoredPosition;
            font = chestGrid.GetComponentInParent<Canvas>().GetComponentsInChildren<TMP_Text>(true).FirstOrDefault()
                ?.font ?? TMP_Settings.defaultFontAsset;
            var canvas = chestGrid.GetComponentInParent<Canvas>().rootCanvas;
            stockPresentation = chestGrid.GetComponent<CanvasGroup>();
            addedPresentation = stockPresentation == null;
            if (addedPresentation)
                stockPresentation = chestGrid.gameObject.AddComponent<CanvasGroup>();
            originalAlpha = stockPresentation.alpha;
            originalRaycasts = stockPresentation.blocksRaycasts;
            originalInteractable = stockPresentation.interactable;
            stockPresentation.alpha = 0;
            stockPresentation.blocksRaycasts = false;
            stockPresentation.interactable = false;
            var root = Rect("Warp Core", chestGrid.transform.parent, new Vector2(0, 1), new Vector2(0, 1), Vector2.zero,
                new Vector2(PanelWidth, PanelHeight));
            window = root.gameObject;
            root.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            var main = canvas.GetComponentsInChildren<InventoryGrid>(true).FirstOrDefault(g =>
                (InventoryType)Field(typeof(InventoryGrid), "source").GetValue(g) ==
                InventoryType.MainInventory);
            inventorySlot = main != null
                ? main.GetComponentsInChildren<InventorySlotUI>(true).FirstOrDefault()?.transform as RectTransform
                : null;
            var nativeFrame = main != null ? FindFrame(main) : FindFrame(chestGrid);
            inventoryFrame = nativeFrame != null
                ? nativeFrame.rectTransform
                : (RectTransform)(main != null ? main.transform : chestGrid.transform);
            var background = root.gameObject.AddComponent<Image>();
            background.color = new Color(.055f, .15f, .21f, .99f);
            if (nativeFrame != null && nativeFrame.sprite != null)
            {
                var borderRect = Rect("Inventory border", root, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
                borderRect.anchorMax = Vector2.one;
                var border = borderRect.gameObject.AddComponent<Image>();
                border.sprite = nativeFrame.sprite;
                border.type = nativeFrame.sprite.border != Vector4.zero ? Image.Type.Sliced : nativeFrame.type;
                border.color = nativeFrame.color;
                border.material = nativeFrame.material;
                border.pixelsPerUnitMultiplier = nativeFrame.pixelsPerUnitMultiplier *
                    nativeFrame.rectTransform.rect.width / PanelWidth;
                border.fillCenter = nativeFrame.fillCenter;
                border.raycastTarget = false;
            }

            matrixTab = Tab("MATRIX", root, new Vector2(24, -22), false);
            coreTab = Tab("CORE", root, new Vector2(218, -22), false);
            nacelleTab = Tab("NACELLES", root, new Vector2(412, -22), true);
            var bay = Rect("Matrix bay", root, new Vector2(0, 1), new Vector2(0, 1), new Vector2(24, -62),
                new Vector2(ContentWidth, DiagnosticsHeight));
            matrixPage = bay.gameObject;
            Label("ANNIHILATION MATRIX", bay, new Vector2(12, -12), new Vector2(548, 24), 16,
                new Color(.5f, .75f, .85f));
            matrixSlotRect.SetParent(bay, false);
            matrixSlotRect.anchorMin = matrixSlotRect.anchorMax = matrixSlotRect.pivot = new Vector2(0, 1);
            matrixSlotRect.localScale = Vector3.one;
            matrixSlotRect.anchoredPosition = new Vector2(12, -48);
            matrixSlotRect.sizeDelta = new Vector2(80, 80);
            matrixDetails = Label("", bay, new Vector2(112, -52), new Vector2(448, 72), 16, Color.white);
            matrixDetails.overflowMode = TextOverflowModes.Truncate;
            var viewport = Rect("Diagnostics", root, new Vector2(0, 1), new Vector2(0, 1), new Vector2(24, -62),
                new Vector2(ContentWidth, DiagnosticsHeight));
            viewport.gameObject.AddComponent<Image>().color = new Color(.035f, .1f, .14f);
            viewport.gameObject.AddComponent<RectMask2D>();
            scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 28;
            var content = Rect("Content", viewport, new Vector2(0, 1), new Vector2(0, 1), Vector2.zero,
                new Vector2(ContentWidth, DiagnosticsHeight));
            footer = Label("", root, new Vector2(24, -356), new Vector2(ContentWidth, 18), 12,
                new Color(.6f, .75f, .82f));
            scroll.viewport = viewport;
            scroll.content = content;
            displayedTab = null;
            displayedInfusers = null;
            fieldValues.Clear();
            nacelles = false;
            matrixSelected = true;
            nextRefresh = 0;
            Refresh();
        }

        private static bool MatrixTooltip(ItemSlotId __0, ItemTooltip ___itemTooltip)
        {
            if (window == null || !matrixSelected || __0.Type != InventoryType.Chest || __0.SlotId != 0)
                return true;
            ___itemTooltip.Hide();
            return false;
        }

        private static Image FindFrame(InventoryGrid source)
        {
            var rect = (RectTransform)source.transform;
            var candidates = new List<Image>();
            for (var node = source.transform; node != null && node.GetComponent<Canvas>() == null; node = node.parent)
            {
                if (node.GetComponentsInChildren<InventoryGrid>(true).Length > 1)
                    break;
                candidates.AddRange(node.GetComponentsInChildren<Image>(true).Where(image =>
                    image.sprite != null && image.GetComponentInParent<InventorySlotUI>() == null &&
                    image.rectTransform.rect.width >= rect.rect.width * .9f &&
                    image.rectTransform.rect.height >= rect.rect.height * .75f));
            }

            return candidates
                .OrderByDescending(image => image.rectTransform.rect.width * image.rectTransform.rect.height)
                .FirstOrDefault() ?? source.GetComponent<Image>();
        }

        private static void Synchronize(InventoryGrid __instance, InventoryType ___source,
            IGenericInventoryProvider ____inventoryProvider)
        {
            if (restoring || ___source != InventoryType.Chest || !__instance.isActiveAndEnabled)
                return;
            var selected = CoreInventory.ClientCore(____inventoryProvider);
            if (selected == null)
            {
                if (chestGrid == __instance)
                    Clear();
                return;
            }

            if (chestGrid != __instance || core != selected)
                Open(__instance, ___source, ____inventoryProvider);
        }

        private static RectTransform Rect(string name, Transform parent, Vector2 anchor, Vector2 rectPivot, Vector2 pos,
            Vector2 dimensions)
        {
            var rect = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = anchor;
            rect.pivot = rectPivot;
            rect.anchoredPosition = pos;
            rect.sizeDelta = dimensions;
            return rect;
        }

        private static TMP_Text Label(string text, Transform parent, Vector2 pos, Vector2 dimensions, float fontSize,
            Color color)
        {
            var rect = Rect("Label", parent, new Vector2(0, 1), new Vector2(0, 1), pos, dimensions);
            var label = rect.gameObject.AddComponent<TextMeshProUGUI>();
            label.font = font;
            label.text = text;
            label.fontSize = fontSize;
            label.color = color;
            label.richText = false;
            label.raycastTarget = false;
            label.overflowMode = TextOverflowModes.Overflow;
            return label;
        }

        private static Button Tab(string title, Transform parent, Vector2 pos, bool showNacelles)
        {
            var rect = Rect(title, parent, new Vector2(0, 1), new Vector2(0, 1), pos, new Vector2(184, 32));
            var image = rect.gameObject.AddComponent<Image>();
            image.color = new Color(.08f, .2f, .26f);
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            Label(title, rect, new Vector2(12, -5), new Vector2(160, 26), 15, Color.white);
            button.onClick.AddListener(() =>
            {
                nacelles = showNacelles;
                matrixSelected = title == "MATRIX";
                nextRefresh = 0;
                if (scroll != null)
                    scroll.verticalNormalizedPosition = 1;
                Refresh();
            });
            return button;
        }

        private static void Refresh()
        {
            if (window == null)
                return;
            if (chestGrid == null || !chestGrid.isActiveAndEnabled || originalParent == null ||
                !originalParent.gameObject.activeInHierarchy || core == null || !core.IsActive ||
                !StriderRegistry.WorldReady)
            {
                Clear();
                return;
            }

            var chest = (RectTransform)chestGrid.transform;
            var root = (RectTransform)window.transform;
            var reference = inventoryFrame != null ? inventoryFrame : chest;
            reference.GetWorldCorners(chestCorners);
            var parent = (RectTransform)root.parent;
            var left = parent.InverseTransformPoint(chestCorners[1]);
            var right = parent.InverseTransformPoint(chestCorners[2]);
            var fit = Vector3.Distance(left, right) / PanelWidth;
            var gap = 10f * fit;
            root.localScale = Vector3.one * fit;
            root.rotation = reference.rotation;
            root.position = parent.TransformPoint(left + new Vector3(0, PanelHeight * fit + gap, 0));
            root.SetSiblingIndex(chest.GetSiblingIndex() + 1);
            if (inventorySlot != null)
            {
                var slotScale = inventorySlot.lossyScale;
                var bayScale = matrixPage.transform.lossyScale;
                matrixSlotRect.localScale = new Vector3(slotScale.x / bayScale.x, slotScale.y / bayScale.y,
                    slotScale.z / bayScale.z);
                matrixSlotRect.sizeDelta = inventorySlot.rect.size;
                var textX = 12 + matrixSlotRect.rect.width * matrixSlotRect.localScale.x + 20;
                matrixDetails.rectTransform.anchoredPosition = new Vector2(textX, -52);
                matrixDetails.rectTransform.sizeDelta = new Vector2(560 - textX, 100);
            }

            if (Time.unscaledTime < nextRefresh)
                return;
            nextRefresh = Time.unscaledTime + .2f;
            var sample = core.State.Get<JObject>("engineering");
            matrixDetails.text = CoreInventory.Read(core, out var grade)
                ? $"Grade {grade.Code} installed   •   {grade.MaxPlasmaRate:F1} plasma/s capacity"
                : "No matrix installed\nInsert a grade A–D matrix";
            coreTab.interactable = matrixSelected || nacelles;
            nacelleTab.interactable = matrixSelected || !nacelles;
            matrixTab.interactable = !matrixSelected;
            matrixPage.SetActive(matrixSelected);
            scroll.gameObject.SetActive(!matrixSelected);
            footer.text = matrixSelected ? "" : "Scroll for more fields  •  Esc to close";
            if (matrixSelected)
                return;
            var infusers = sample?["diagnostics"]?["infusers"] as JArray ?? new JArray();
            var ids = string.Join(",", infusers.Select(p => p["infuserId"]?.ToString()));
            if (displayedTab != nacelles || displayedInfusers != ids)
                BuildFields(infusers);
            if (sample == null || !sample.HasValues)
            {
                foreach (var value in fieldValues.Values)
                    value.text = "Waiting…";
                return;
            }

            if (!nacelles)
            {
                Set("Status", sample["status"]);
                Set("Warp eligibility",
                    sample["diagnostics"]?["warpRestriction"]?.Type == JTokenType.String
                        ? sample["diagnostics"]["warpRestriction"]
                        : "Allowed");
                Set("Production", sample["diagnostics"]?["productionStatus"]);
                Set("Hydrogen", Number(sample["hydrogenRate"], "F3", " units/s"));
                Set("Plasma output", Number(sample["plasmaOutput"], "F2", " /s"));
                Set("Plasma demand", Number(sample["plasmaDemand"], "F2", " /s"));
                Set("Charge time", Number(sample["chargeSeconds"], "F1", " s"));
                Set("Matched coil pairs", sample["matchedPairs"]);
                Set("Optimal coils", sample["diagnostics"]?["recommendedCoils"]);
                Set("Field strength", Number(sample["fieldStrength"], "F2", ""));
                Set("Required field", Number(sample["recommendedFieldStrength"], "F2", ""));
                Set("Target speed", Number(sample["targetSpeed"], "N0", " m/s"));
                Set("Measured speed", Number(sample["measuredSpeed"], "N0", " m/s"));
                Set("Layout",
                    sample["diagnostics"]?["layoutBlocker"]?.Type == JTokenType.String
                        ? sample["diagnostics"]["layoutBlocker"]
                        : "Ready");
                Set("Warp entry",
                    sample["diagnostics"]?["warpEntryFailure"]?.Type == JTokenType.String
                        ? sample["diagnostics"]["warpEntryFailure"]
                        : "Ready");
            }
            else
            {
                for (var i = 0; i < infusers.Count; i++)
                {
                    var p = infusers[i];
                    var key = i + ":";
                    Set(key + "Coils", p["validCoils"]);
                    Set(key + "Optimal coils", sample["diagnostics"]?["recommendedCoilsPerNacelle"]);
                    Set(key + "Plasma connection", (bool?)p["conduitConnected"] == true ? "Connected" : "Disconnected");
                    Set(key + "Delivered", Number(p["deliveredRate"], "F2", " plasma/s"));
                    Set(key + "Demand", Number(p["plasmaDemand"], "F2", " plasma/s"));
                    Set(key + "Charge", Number(p["charge"], "P0", ""));
                    Set(key + "Coil chain", p["issue"]?.Type == JTokenType.String ? p["issue"] : "Ready");
                }
            }

            LayoutFields(infusers);
        }

        private static string Number(JToken value, string format, string unit) =>
            value == null || value.Type == JTokenType.Null ? "Unavailable" : ((double)value).ToString(format) + unit;

        private static void Set(string name, object value)
        {
            if (fieldValues.TryGetValue(name, out var label))
            {
                var text = value?.ToString();
                label.text = string.IsNullOrWhiteSpace(text) ? "None" : text;
            }
        }

        private static void BuildFields(JArray infusers)
        {
            foreach (Transform child in scroll.content)
            {
                child.gameObject.SetActive(false);
                Destroy(child.gameObject);
            }

            fieldValues.Clear();
            displayedTab = nacelles;
            displayedInfusers = string.Join(",", infusers.Select(p => p["infuserId"]?.ToString()));
            var y = 8f;
            if (!nacelles)
            {
                foreach (var name in CoreFields)
                {
                    AddField(name, name, 0, y, ContentWidth);
                    y += name == "Production" || name == "Layout" || name == "Warp entry" ? 60 : 32;
                }
            }
            else if (infusers.Count == 0)
            {
                Label("No infusers installed. Install two aligned infusers.", scroll.content, new Vector2(12, -12),
                    new Vector2(548, 60), 16, Color.white);
                y = 80;
            }
            else
            {
                for (var i = 0; i < infusers.Count; i++)
                {
                    float x = i % 2 * NacelleWidth, top = i / 2 * 350;
                    var heading = Label($"INFUSER {i + 1}", scroll.content, new Vector2(x + 12, -top - 8),
                        new Vector2(262, 26), 16, new Color(.5f, .85f, 1));
                    heading.gameObject.name = "Infuser heading " + i;
                    var row = top + 44;
                    foreach (var name in NacelleFields)
                    {
                        AddField(i + ":" + name, name, x, row, NacelleWidth);
                        row += 40;
                    }

                    y = Mathf.Max(y, row + 20);
                }
            }

            scroll.content.sizeDelta = new Vector2(ContentWidth, Mathf.Max(DiagnosticsHeight, y));
            scroll.verticalNormalizedPosition = 1;
        }

        private static void LayoutFields(JArray infusers)
        {
            var bottom = 8f;
            if (!nacelles)
            {
                foreach (Transform child in scroll.content)
                {
                    var label = fieldValues.Values.FirstOrDefault(v => v.transform.parent == child);
                    if (label != null)
                        bottom += FitField(label, bottom);
                }
            }
            else
            {
                for (var pair = 0; pair < infusers.Count; pair += 2)
                {
                    var next = bottom;
                    for (var i = pair; i < Min(pair + 2, infusers.Count); i++)
                    {
                        var heading = scroll.content.Find("Infuser heading " + i) as RectTransform;
                        if (heading != null)
                            heading.anchoredPosition = new Vector2(i % 2 * NacelleWidth + 12, -bottom);
                        var y = bottom + 36;
                        foreach (var name in NacelleFields)
                            if (fieldValues.TryGetValue(i + ":" + name, out var value))
                                y += FitField(value, y);
                        next = Mathf.Max(next, y + 16);
                    }

                    bottom = next;
                }
            }

            scroll.content.sizeDelta = new Vector2(ContentWidth, Mathf.Max(DiagnosticsHeight, bottom));
        }

        private static float FitField(TMP_Text value, float y)
        {
            var row = (RectTransform)value.transform.parent;
            var height = Mathf.Max(32,
                value.GetPreferredValues(value.text, value.rectTransform.rect.width, 0).y + 12);
            var title = row.GetComponentsInChildren<TMP_Text>().First(t => t != value);
            height = Mathf.Max(height, title.GetPreferredValues(title.text, title.rectTransform.rect.width, 0).y + 12);
            row.anchoredPosition = new Vector2(row.anchoredPosition.x, -y);
            row.sizeDelta = new Vector2(row.sizeDelta.x, height);
            value.rectTransform.sizeDelta = new Vector2(value.rectTransform.sizeDelta.x, height - 8);
            return height + 4;
        }

        private static void AddField(string key, string title, float x, float y, float width)
        {
            var row = Rect(title, scroll.content, new Vector2(0, 1), new Vector2(0, 1), new Vector2(x, -y),
                new Vector2(width - 4, 30));
            row.gameObject.AddComponent<Image>().color = new Color(.065f, .16f, .21f);
            float labelWidth = width > 300 ? 170 : 126;
            Label(title, row, new Vector2(10, -4), new Vector2(labelWidth - 14, 28), 14, new Color(.6f, .77f, .85f));
            var value = Label("—", row, new Vector2(labelWidth, -4), new Vector2(width - labelWidth - 14, 56), 14,
                Color.white);
            fieldValues[key] = value;
        }

        private static void Closed(InventoryGrid __instance)
        {
            if (__instance == chestGrid && !restoring)
                Clear();
        }

        internal static void Clear()
        {
            restoring = true;
            if (matrixSlotRect != null && originalParent != null)
            {
                matrixSlotRect.SetParent(originalParent, false);
                matrixSlotRect.SetSiblingIndex(sibling);
                matrixSlotRect.anchorMin = anchorMin;
                matrixSlotRect.anchorMax = anchorMax;
                matrixSlotRect.pivot = pivot;
                matrixSlotRect.sizeDelta = size;
                matrixSlotRect.anchoredPosition = position;
                matrixSlotRect.localScale = originalScale;
            }

            if (stockPresentation != null)
            {
                stockPresentation.alpha = originalAlpha;
                stockPresentation.blocksRaycasts = originalRaycasts;
                stockPresentation.interactable = originalInteractable;
            }

            stockPresentation = null;
            inventoryFrame = null;
            inventorySlot = null;
            fieldValues.Clear();
            displayedTab = null;
            if (window != null)
            {
                window.SetActive(false);
                Destroy(window);
            }

            window = null;
            matrixPage = null;
            matrixTab = null;
            matrixSelected = false;
            chestGrid = null;
            core = null;
            matrixSlotRect = null;
            originalParent = null;
            restoring = false;
        }

        internal static void Shutdown()
        {
            Canvas.preWillRenderCanvases -= Refresh;
            Clear();
        }
    }
}
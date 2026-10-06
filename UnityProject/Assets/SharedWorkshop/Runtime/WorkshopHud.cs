using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace SharedWorkshop.Runtime
{
    [DisallowMultipleComponent]
    public sealed class WorkshopHud : MonoBehaviour
    {
        private static readonly Color Ink = new Color(0.055f, 0.09f, 0.13f, 0.94f);
        private static readonly Color Panel = new Color(0.055f, 0.09f, 0.13f, 0.88f);
        private static readonly Color Accent = new Color(0.08f, 0.68f, 0.63f, 1f);
        private SharedWorkshopController _controller;
        private WorkshopNetworkClient _network;
        private InputField _serverField;
        private InputField _roomField;
        private InputField _nameField;
        private Text _statusText;
        private Text _diagnosticsText;
        private Button _joinButton;
        private Button _alignButton;
        private Button _deleteButton;
        private Button _rotateButton;
        private float _nextRefresh;

        public void Initialize(SharedWorkshopController controller, WorkshopNetworkClient network)
        {
            _controller = controller;
            _network = network;
            BuildCanvas();
            _network.StatusChanged += RefreshText;
            _network.Joined += RefreshText;
            _controller.SetSelectedColor(0);
        }

        private void BuildCanvas()
        {
            if (FindFirstObjectByType<EventSystem>() == null)
            {
                var eventSystem = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
                DontDestroyOnLoad(eventSystem);
            }

            var canvasObject = new GameObject("Shared Workshop HUD", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.matchWidthOrHeight = 0.5f;

            var top = CreatePanel(canvasObject.transform, "Connection Panel", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -18f), new Vector2(-36f, 500f));
            var topLayout = top.gameObject.AddComponent<VerticalLayoutGroup>();
            topLayout.padding = new RectOffset(16, 16, 14, 14);
            topLayout.spacing = 8f;
            topLayout.childControlWidth = true;
            topLayout.childControlHeight = true;
            topLayout.childForceExpandWidth = true;
            topLayout.childForceExpandHeight = false;

            CreateLabel(top, "Shared AR Workshop", 28, FontStyle.Bold, 44f, Color.white);
            _serverField = CreateInput(top, "HTTPS server URL", PlayerPrefs.GetString("SharedWorkshop.Api", ""), 54f);

            var roomRow = CreateRow(top, "Room");
            _roomField = CreateInput(roomRow, "Room code", PlayerPrefs.GetString("SharedWorkshop.Room", ""), 56f);
            _roomField.characterLimit = 12;
            _roomField.contentType = InputField.ContentType.Alphanumeric;
            var randomButton = CreateButton(roomRow, "Random code", Accent, 19);
            randomButton.onClick.AddListener(GenerateRoomCode);
            SetFlexible(randomButton.gameObject, 0.6f);

            _nameField = CreateInput(top, "Display name", PlayerPrefs.GetString("SharedWorkshop.Name", "Builder"), 54f);
            _joinButton = CreateButton(top, "Join room", Accent, 23);
            _joinButton.onClick.AddListener(JoinRoom);

            _statusText = CreateLabel(top, "AR status", 20, FontStyle.Bold, 42f, Color.white);
            _diagnosticsText = CreateLabel(top, "", 17, FontStyle.Normal, 42f, new Color(0.76f, 0.83f, 0.87f));
            CreateLabel(top, "Camera images and room scans stay on this device. Only block transforms are sent.", 16, FontStyle.Normal, 34f, new Color(0.58f, 0.81f, 0.77f));

            var bottom = CreatePanel(canvasObject.transform, "Workshop tools", new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 18f), new Vector2(-36f, 360f));
            var bottomLayout = bottom.gameObject.AddComponent<VerticalLayoutGroup>();
            bottomLayout.padding = new RectOffset(12, 12, 12, 12);
            bottomLayout.spacing = 8f;
            bottomLayout.childControlWidth = true;
            bottomLayout.childControlHeight = true;
            bottomLayout.childForceExpandWidth = true;
            bottomLayout.childForceExpandHeight = false;

            var actionRow = CreateRow(bottom, "Actions");
            _alignButton = CreateButton(actionRow, "Align room", Accent, 18);
            _alignButton.onClick.AddListener(_controller.BeginAlignment);
            _deleteButton = CreateButton(actionRow, "Delete: Off", new Color(0.23f, 0.28f, 0.34f), 18);
            _deleteButton.onClick.AddListener(() => { _controller.ToggleDeleteMode(); RefreshButtons(); });
            _rotateButton = CreateButton(actionRow, "Rotate: Off", new Color(0.23f, 0.28f, 0.34f), 18);
            _rotateButton.onClick.AddListener(() => { _controller.ToggleRotateMode(); RefreshButtons(); });
            var drop = CreateButton(actionRow, "Gravity", new Color(0.23f, 0.28f, 0.34f), 18);
            drop.onClick.AddListener(_controller.DropAllBlocks);

            for (var rowIndex = 0; rowIndex < 2; rowIndex++)
            {
                var row = CreateRow(bottom, "Colors " + rowIndex);
                for (var column = 0; column < 3; column++)
                {
                    var colorIndex = rowIndex * 3 + column;
                    var swatch = CreateButton(row, (colorIndex + 1).ToString(), SharedWorkshopController.ColorAt(colorIndex), 22);
                    swatch.onClick.AddListener(() => _controller.SetSelectedColor(colorIndex));
                }
            }

            var saveRow = CreateRow(bottom, "Save and load");
            var save = CreateButton(saveRow, "Save", new Color(0.23f, 0.28f, 0.34f), 18);
            save.onClick.AddListener(_controller.SaveProject);
            var load = CreateButton(saveRow, "Load", new Color(0.23f, 0.28f, 0.34f), 18);
            load.onClick.AddListener(_controller.LoadProject);
            RefreshButtons();
        }

        private void Update()
        {
            if (Time.unscaledTime < _nextRefresh) return;
            _nextRefresh = Time.unscaledTime + 0.2f;
            RefreshText();
            RefreshButtons();
        }

        private void JoinRoom()
        {
            var code = _roomField.text.Trim().ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(code))
            {
                GenerateRoomCode();
                code = _roomField.text;
            }
            PlayerPrefs.SetString("SharedWorkshop.Api", _serverField.text.Trim());
            PlayerPrefs.SetString("SharedWorkshop.Room", code);
            PlayerPrefs.SetString("SharedWorkshop.Name", _nameField.text.Trim());
            PlayerPrefs.Save();
            _controller.JoinRoom(_serverField.text, code, _nameField.text);
        }

        private void GenerateRoomCode()
        {
            const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
            var builder = new StringBuilder(8);
            for (var i = 0; i < 8; i++) builder.Append(alphabet[Random.Range(0, alphabet.Length)]);
            _roomField.text = builder.ToString();
        }

        private void RefreshText()
        {
            if (_statusText != null) _statusText.text = _controller.Status;
            if (_diagnosticsText != null) _diagnosticsText.text = _controller.Diagnostics();
        }

        private void RefreshText(string _) { RefreshText(); }
        private void RefreshText(WorkshopJoinResponse _) { RefreshText(); }

        private void RefreshButtons()
        {
            if (_joinButton != null) _joinButton.interactable = !_network.IsConnected;
            if (_alignButton != null) _alignButton.interactable = _network.IsConnected;
            if (_deleteButton != null) _deleteButton.GetComponentInChildren<Text>().text = _controller.IsDeleteMode ? "Delete: On" : "Delete: Off";
            if (_rotateButton != null) _rotateButton.GetComponentInChildren<Text>().text = _controller.IsRotateMode ? "Rotate: On" : "Rotate: Off";
        }

        private static RectTransform CreatePanel(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 position, Vector2 size)
        {
            var panel = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            panel.transform.SetParent(parent, false);
            var rect = panel.GetComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = new Vector2(0.5f, anchorMin.y == 1f ? 1f : 0f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            panel.GetComponent<Image>().color = Panel;
            panel.GetComponent<LayoutElement>().ignoreLayout = true;
            return rect;
        }

        private static RectTransform CreateRow(Transform parent, string name)
        {
            var row = new GameObject(name, typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            row.transform.SetParent(parent, false);
            var layout = row.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = 8f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = true;
            var element = row.GetComponent<LayoutElement>();
            element.minHeight = 58f;
            element.preferredHeight = 58f;
            return row.GetComponent<RectTransform>();
        }

        private static Text CreateLabel(Transform parent, string value, int fontSize, FontStyle style, float height, Color color)
        {
            var labelObject = new GameObject("Label", typeof(RectTransform), typeof(Text), typeof(LayoutElement));
            labelObject.transform.SetParent(parent, false);
            var text = labelObject.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            text.fontSize = fontSize;
            text.fontStyle = style;
            text.color = color;
            text.text = value;
            text.alignment = TextAnchor.MiddleLeft;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.raycastTarget = false;
            var element = labelObject.GetComponent<LayoutElement>();
            element.minHeight = height;
            element.preferredHeight = height;
            return text;
        }

        private static InputField CreateInput(Transform parent, string placeholder, string initialValue, float height)
        {
            var fieldObject = new GameObject("Input Field", typeof(RectTransform), typeof(Image), typeof(InputField), typeof(LayoutElement));
            fieldObject.transform.SetParent(parent, false);
            var background = fieldObject.GetComponent<Image>();
            background.color = new Color(0.12f, 0.17f, 0.21f, 1f);
            var text = CreateTextChild(fieldObject.transform, "Value", 20, Color.white);
            var hint = CreateTextChild(fieldObject.transform, "Placeholder", 19, new Color(0.57f, 0.65f, 0.7f));
            hint.text = placeholder;
            hint.fontStyle = FontStyle.Italic;
            var input = fieldObject.GetComponent<InputField>();
            input.textComponent = text;
            input.placeholder = hint;
            input.targetGraphic = background;
            input.text = initialValue;
            var rect = fieldObject.GetComponent<RectTransform>();
            rect.offsetMin = new Vector2(0f, 0f);
            rect.offsetMax = new Vector2(0f, 0f);
            var element = fieldObject.GetComponent<LayoutElement>();
            element.minHeight = height;
            element.preferredHeight = height;
            return input;
        }

        private static Button CreateButton(Transform parent, string label, Color color, int fontSize)
        {
            var buttonObject = new GameObject("Button " + label, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            buttonObject.transform.SetParent(parent, false);
            var image = buttonObject.GetComponent<Image>();
            image.color = color;
            var button = buttonObject.GetComponent<Button>();
            button.targetGraphic = image;
            var colors = button.colors;
            colors.highlightedColor = Color.Lerp(color, Color.white, 0.16f);
            colors.pressedColor = Color.Lerp(color, Color.black, 0.18f);
            colors.disabledColor = new Color(color.r, color.g, color.b, 0.45f);
            button.colors = colors;
            var text = CreateTextChild(buttonObject.transform, "Text", fontSize, Color.white);
            var luminance = color.r * 0.2126f + color.g * 0.7152f + color.b * 0.0722f;
            text.color = luminance > 0.62f ? Ink : Color.white;
            text.fontStyle = FontStyle.Bold;
            text.alignment = TextAnchor.MiddleCenter;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            button.transition = Selectable.Transition.ColorTint;
            var element = buttonObject.GetComponent<LayoutElement>();
            element.minHeight = 58f;
            element.preferredHeight = 58f;
            return button;
        }

        private static Text CreateTextChild(Transform parent, string name, int fontSize, Color color)
        {
            var textObject = new GameObject(name, typeof(RectTransform), typeof(Text));
            textObject.transform.SetParent(parent, false);
            var rect = textObject.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(14f, 6f);
            rect.offsetMax = new Vector2(-14f, -6f);
            var text = textObject.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            text.fontSize = fontSize;
            text.color = color;
            text.alignment = TextAnchor.MiddleLeft;
            text.raycastTarget = false;
            return text;
        }

        private static void SetFlexible(GameObject target, float value)
        {
            var element = target.GetComponent<LayoutElement>();
            if (element != null) element.flexibleWidth = value;
        }
    }
}

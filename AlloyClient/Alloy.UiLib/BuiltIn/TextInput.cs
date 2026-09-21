using System;
using System.Text;
using Alloy.UiLib.Core;
using Alloy.UiLib.Data;
using Alloy.UiLib.Input;
using Alloy.UiLib.Rendering;
using OpenTK.Mathematics;
using OpenTK.Platform;

namespace Alloy.UiLib.BuiltIn;

public struct InputConfig {
    public int X = 0;
    public int Y = 0;
    public float FontSize = 10;
    public FontType FontType = FontType.Normal;
    public FontGroup FontGroup = FontGroup.MyriadPro;
    public uint Color = 0xFFFFFF;
    public uint OutlineColor = 0x0;
    public uint OutlineThickness = 4;
    public uint BoxColor = 0xFFFFFF;
    public string BoxSlice = TextInput.BoxLookup;
    public int Width = 100;
    public string DefaultText = "";
    public byte MaxCharacters = byte.MaxValue;
    public bool Password = false;
    public bool ClickToActivate = true;
    public Action OnFocus = null;
    public Action OnUnfocus = null;
    public UiAnchor Anchor = UiAnchor.LeftTop;
    
    public Action OnChange = null;
    
    public InputConfig() { }
}

public sealed class TextInput : Sprite {

    public const string BoxLookup = "textBox";

    private const int CutX = 2;
    private const int CutY = 2;

    internal static TextInput ActiveInput;
    
    public string Text => _inputText.ToString();
    private readonly StringBuilder _inputText = new();
    private bool _isDefaultText = true;
    
    private readonly float _fontScale;
    private readonly BitmapFont _font;
    private readonly float _outlineThickness;
    private readonly int _width;
    private readonly string _defaultText;
    private readonly byte _maxCharacters;
    private readonly bool _password;
    private readonly bool _clickActivate;
    private readonly Action _onFocus;
    private readonly Action _onUnfocus;

    private readonly NineSliceRect _textBox;
    private readonly SimpleText _caret;

    // The typed characters are drawn by this child, added AFTER the background box. (They used to be drawn by the input itself, and a sprite is drawn before its
    // children - so a box whose middle is not transparent, like the walnut slot the chat and the Bug Board use, was painted over the text: a solid blue bar.)
    private readonly GlyphLayer _glyphs = new();
    private VertexUi[] _vertexData = [];
    private int _caretIndex = -1;
    private bool _isCaretActive = false;
    private double _lastCaretUpdateTime;
    private int _startIndex;
    private bool _unFocusOnClick = false;

    private Vector2i _mousePosition;

    public TextInput(InputConfig config) {
        X = config.X;
        Y = config.Y;
        _fontScale = config.FontSize;
        _font = UiRender.GetFont(config.FontGroup, config.FontType);
        SetColor(config.Color);
        SetColorSecondary(config.OutlineColor);
        _outlineThickness = _font.ValidateOutlineSize(config.OutlineThickness);
        _width = config.Width;
        _defaultText = config.DefaultText;
        _maxCharacters = config.MaxCharacters;
        _password = config.Password;
        _clickActivate = config.ClickToActivate;
        _onFocus = config.OnFocus;
        _onUnfocus = config.OnUnfocus;
        SetAnchor(config.Anchor);

        MouseEnabled = true;

        _inputText.Append(config.DefaultText);

        var rectConfig = new NineSliceConfig { Width = _width, Height = (int)(_font.LineHeight * _fontScale) + CutY * 3, SliceData = config.BoxSlice, CutX = CutX, CutY = CutY};
        _textBox = new NineSliceRect(rectConfig);
        _textBox.SetColor(config.BoxColor);
        AddChild(_textBox);

        _glyphs.Setup(config.Color, config.OutlineColor, _outlineThickness);
        AddChild(_glyphs);

        var caretConfig = new TextConfig { Text = "|", FontSize = config.FontSize, FontType = config.FontType, FontGroup = config.FontGroup, Color = config.Color, OutlineColor = config.OutlineColor, OutlineThickness = (int)_outlineThickness };
        _caret = new SimpleText(caretConfig);
        _caret.Visible = false;
        AddChild(_caret);
        
        SetHitboxType(CollisionType.CustomNoScale);
        
        AddEventListener(MouseEvent.LeftClick, OnMouseClick);
        
        ResizeBackBuffer();
        FillData();
    }
    
    private void ResizeBackBuffer() {
        var size = _maxCharacters + 1;
        _vertexData = new VertexUi[size * 4];
        var indices = new ushort[size * 6];
        for (var i = 0; i < indices.Length / 6; i++) {
            var idx6 = i * 6;
            var idx4 = i * 4;

            indices[idx6] = (ushort)(0 + idx4);
            indices[idx6 + 1] = (ushort)(1 + idx4);
            indices[idx6 + 2] = (ushort)(2 + idx4);
            indices[idx6 + 3] = (ushort)(0 + idx4);
            indices[idx6 + 4] = (ushort)(2 + idx4);
            indices[idx6 + 5] = (ushort)(3 + idx4);
        }

        _glyphs.SetBuffers(_vertexData, indices);
    }
    
    private void OnFrameEnter() {
        if (!_isCaretActive) return;
        var gameTime = Stage.GameTime;
        if (gameTime.TotalMs - _lastCaretUpdateTime < 500) return;

        _lastCaretUpdateTime = gameTime.TotalMs;
        _caret.Visible = !_caret.Visible;
    }

    private void FillData() {
        var startX = CutX * 3;
        var startY = _font.Ascender * _fontScale + CutY * 3;
        var zero = new Vector2(startX, startY);

        var (start, end) = _font.GetStartIndex(_inputText, _caretIndex, _width - startX * 2 - _caret.Width, _outlineThickness, _fontScale);
        _startIndex = start;
        var primitives = 2;
        
        var idx = 4;
        var len = _inputText.Length;
        var caret = false;

        var password = _password && !_isDefaultText;
        
        for (var i = start; i < end; i++) {

            if (!caret && i == _caretIndex) {
                caret = true;
                i--;
                
                _caret.X = (int)zero.X;
                _caret.Y = CutY * 3;
                zero.X += _caret.Width;
                
                continue;
            }
            
            var c = password ? '*' : _inputText[i];
            switch (c) {
                case '\n':
                case '\r': 
                    continue;
                default:
                    if (!_font.Glyphs.TryGetValue(c, out var glyph)) continue;

                    var uv = glyph.UV;
                    var pos = glyph.Position;
                    
                    _vertexData[idx + 0] = new VertexUi(new Vector2(zero.X + pos.X0 * _fontScale, zero.Y - pos.Y1 * _fontScale), new Vector2(uv.X0, uv.Y1)); //bl
                    _vertexData[idx + 1] = new VertexUi(new Vector2(zero.X + pos.X0 * _fontScale, zero.Y - pos.Y0 * _fontScale), new Vector2(uv.X0, uv.Y0)); //tl
                    _vertexData[idx + 2] = new VertexUi(new Vector2(zero.X + pos.X1 * _fontScale, zero.Y - pos.Y0 * _fontScale), new Vector2(uv.X1, uv.Y0)); //tr
                    _vertexData[idx + 3] = new VertexUi(new Vector2(zero.X + pos.X1 * _fontScale, zero.Y - pos.Y1 * _fontScale), new Vector2(uv.X1, uv.Y1)); //br

                    if (i < len - 1) {
                        var k = password ? '*' : _inputText[i + 1];
                        _font.Kernings.TryGetValue((c, k), out var kern);
                        zero.X += kern * _fontScale;
                    }

                    zero.X += glyph.Advance * _fontScale;
                    idx += 4;
                    primitives += 2;
                    continue;
            }
        }

        if (!caret) {
            _caret.X = (int)zero.X;
            _caret.Y = CutY * 3;
        }
        
        _glyphs.Show(primitives);
    }

    protected override bool CustomHitbox(Vector2i pos) {
        var hit = pos.X > 0 && pos.X < _textBox.Width && pos.Y > 0 && pos.Y < _textBox.Height;

        _unFocusOnClick = !hit && ActiveInput == this;
        
        _mousePosition = pos;
        return hit;
    }

    private void OnMouseClick(MouseEvent args) {
        if (_unFocusOnClick) {
            UnFocus();
            return;
        }
        
        
        if (ActiveInput != this && _clickActivate) {
            ActiveInput?.UnFocus();
            Focus();
        }
        
        SetCaretIndex();
    }

    private void SetCaretIndex() {
        var i = 1;// Offset by 1 for rect
        for (var j = _startIndex; j < _inputText.Length; j++) {
            var p1 = _vertexData[i * 4 + 1].Position.X;
            var p2 = _vertexData[i * 4 + 3].Position.X;
            var half = (p2 - p1) / 2f;

            if (j == _startIndex && _mousePosition.X <= p1) {
                _caretIndex = 0;
            } else if (_mousePosition.X >= p1 && _mousePosition.X < p1 + half) {
                _caretIndex = _startIndex + i - 1;
            } else if (_mousePosition.X <= p2 && _mousePosition.X >= p2 - half) {
                _caretIndex = _startIndex + i;
            } else if (j + 1 == _inputText.Length && _mousePosition.X >= p2) {
                _caretIndex = -1;
            }

            i++;
        }
        
        FillData();
    }
    
    internal void OnManualTextInput(Key key) {
        switch (key) {
            case Key.Backspace when _inputText.Length > 0:
                if (_caretIndex == -1) {
                    _inputText.Remove(_inputText.Length - 1, 1);
                } else if (_caretIndex > 0) {
                    _caretIndex--;
                    _inputText.Remove(_caretIndex, 1);
                }
                FillData();
                break;
            case Key.Delete when _caretIndex < _inputText.Length && _caretIndex >= 0:
                _inputText.Remove(_caretIndex, 1);
                if (_caretIndex == _inputText.Length) {
                    _caretIndex = -1;
                }
                FillData();
                break;
            case Key.C when Stage.Keyboard.IsOnlyCtrlDown() && Toolkit.Clipboard.GetClipboardFormat() == ClipboardFormat.Text:
                //todo
                //Toolkit.Clipboard.SetClipboardText();
                break;
            case Key.V when Stage.Keyboard.IsOnlyCtrlDown() && Toolkit.Clipboard.GetClipboardFormat() == ClipboardFormat.Text:
                var text = Toolkit.Clipboard.GetClipboardText();

                if (string.IsNullOrEmpty(text)) {
                    return;
                }
                
                var span = text.AsSpan(0, Math.Min(text.Length, _maxCharacters - _inputText.Length));
                
                foreach (var input in span) { // Not ideal for long pastes but need to filter for invalid characters
                    AddChar(input);
                }
                
                FillData();
                break;
            case Key.A when Stage.Keyboard.IsOnlyCtrlDown():
                //todo
                break;
            case Key.LeftArrow:
                if (_caretIndex > 0) {
                    _caretIndex--;
                    FillData();
                }

                if (_caretIndex == -1) {
                    _caretIndex = _inputText.Length - 1;
                    FillData();
                }
                break;
            case Key.RightArrow:
                if (_caretIndex != -1) {
                    _caretIndex++;

                    if (_caretIndex == _inputText.Length) {
                        _caretIndex = -1;
                    }
                    FillData();
                }
                break;
        }
    }

    internal void OnTextInput(ReadOnlySpan<char> text) {
        if (text.Length != 1) {
            return;
        }
        
        AddChar(text[0]);
        
        FillData();
    }

    private void AddChar(char input) {
        if (char.IsControl(input)) return;
        if (char.IsWhiteSpace(input) && input != ' ') return;
        if (!_font.Glyphs.ContainsKey(input)) return;
        if (_inputText.Length == _maxCharacters) return;
        
        if (_caretIndex == -1) {
            _inputText.Append(input);
        } else {
            _inputText.Insert(_caretIndex, input);
            _caretIndex++;
        }
    }

    public bool HasText(bool ignoreWhitespace) {
        if (ignoreWhitespace) {
            return !string.IsNullOrWhiteSpace(_inputText.ToString());
        }
        
        return _inputText.Length > 0;
    }

    public void Focus() {
        if (ActiveInput != this) {
            ActiveInput?.UnFocus();
            ActiveInput = this;
        }
        
        _isCaretActive = true;
        _caret.Visible = true;
        _caretIndex = -1;
        _onFocus?.Invoke();

        ClearIfDefault();
        
        AddEventListener(Event.EnterFrame, OnFrameEnter);
        
        FillData();
    }

    public void UnFocus(bool clearText = false) {
        ActiveInput = null;
        _isCaretActive = false;
        _caretIndex = -1;
        _caret.Visible = false;
        _onUnfocus?.Invoke();

        if (clearText)
            _inputText.Clear();
        
        if (_inputText.Length == 0) {
            SetDefault();
        }
        
        RemoveEventListener(Event.EnterFrame, OnFrameEnter);
        
        FillData();
    }
    
    public void InsertText(string text) {
        ClearIfDefault();
        
        if (_caretIndex == -1) {
            _inputText.Append(text);
        } else {
            _inputText.Insert(_caretIndex, text);
            _caretIndex += text.Length;
        }
        
        FillData();
    }

    public void SetText(string text) {
        if (text == string.Empty) {
            SetDefault();
            return;
        }

        ClearIfDefault();
        _inputText.Append(text);
        FillData();
    }

    private void ClearIfDefault() {
        if (!_isDefaultText) {
            return;
        }
        
        _inputText.Clear();
        _isDefaultText = false;
    }

    private void SetDefault() {
        _inputText.Clear();
        _inputText.Append(_defaultText);
        _isDefaultText = true;
    }
}

// Draws the characters of a TextInput (see the note on TextInput._glyphs).
internal sealed class GlyphLayer : Sprite {
    public GlyphLayer() {
        TextureId = TextureType.Text;
    }

    public void Setup(uint color, uint outlineColor, float outlineThickness) {
        SetColor(color);
        SetColorSecondary(outlineColor);
        Extra1.X = outlineThickness;
    }

    public void SetBuffers(VertexUi[] vertices, ushort[] indices) {
        VertexData = vertices;
        Indices = indices;
    }

    // primitives: the number of triangles that hold characters (the first quad is unused, as before).
    public void Show(int primitives) {
        OverridePrimCount = primitives;
        SetGraphicsBuffer();
    }
}

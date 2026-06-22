using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;
using System.Windows;
using System.Windows.Media;

namespace MiniCursorAgent.Services;

public sealed class EditorDiffHighlighter
{
    private readonly TextEditor _editor;
    private readonly DiffLineBackgroundRenderer _backgroundRenderer;
    private readonly DiffLineColorizer _colorizer;
    private IReadOnlyList<DiffLineKind> _lineKinds = Array.Empty<DiffLineKind>();

    public EditorDiffHighlighter(TextEditor editor)
    {
        _editor = editor;
        _backgroundRenderer = new DiffLineBackgroundRenderer(() => _lineKinds);
        _colorizer = new DiffLineColorizer(() => _lineKinds);

        _editor.TextArea.TextView.BackgroundRenderers.Add(_backgroundRenderer);
        _editor.TextArea.TextView.LineTransformers.Add(_colorizer);
    }

    public bool IsActive { get; private set; }

    public void Apply(IReadOnlyList<DiffLineKind> lineKinds)
    {
        _lineKinds = lineKinds;
        IsActive = true;
        Invalidate();
    }

    public void Clear()
    {
        _lineKinds = Array.Empty<DiffLineKind>();
        IsActive = false;
        Invalidate();
    }

    private void Invalidate()
    {
        _editor.TextArea.TextView.InvalidateLayer(KnownLayer.Background);
        _editor.TextArea.TextView.Redraw();
    }

    private sealed class DiffLineBackgroundRenderer : IBackgroundRenderer
    {
        private readonly Func<IReadOnlyList<DiffLineKind>> _getLineKinds;

        public DiffLineBackgroundRenderer(Func<IReadOnlyList<DiffLineKind>> getLineKinds)
        {
            _getLineKinds = getLineKinds;
        }

        public KnownLayer Layer => KnownLayer.Background;

        public void Draw(TextView textView, DrawingContext drawingContext)
        {
            var lineKinds = _getLineKinds();
            if (lineKinds.Count == 0)
            {
                return;
            }

            textView.EnsureVisualLines();

            foreach (var visualLine in textView.VisualLines)
            {
                var lineIndex = visualLine.FirstDocumentLine.LineNumber - 1;
                if (lineIndex < 0 || lineIndex >= lineKinds.Count)
                {
                    continue;
                }

                var brush = lineKinds[lineIndex] switch
                {
                    DiffLineKind.Added => new SolidColorBrush(Color.FromRgb(0xD9, 0xF2, 0xD9)),
                    DiffLineKind.Removed => new SolidColorBrush(Color.FromRgb(0xFF, 0xD6, 0xD6)),
                    _ => null
                };

                if (brush is null)
                {
                    continue;
                }

                brush.Freeze();
                var top = visualLine.GetTextLineVisualYPosition(visualLine.TextLines[0], VisualYPosition.LineTop);
                drawingContext.DrawRectangle(brush, null, new Rect(0, top, textView.ActualWidth, visualLine.Height));
            }
        }
    }

    private sealed class DiffLineColorizer : DocumentColorizingTransformer
    {
        private readonly Func<IReadOnlyList<DiffLineKind>> _getLineKinds;

        public DiffLineColorizer(Func<IReadOnlyList<DiffLineKind>> getLineKinds)
        {
            _getLineKinds = getLineKinds;
        }

        protected override void ColorizeLine(DocumentLine line)
        {
            var lineKinds = _getLineKinds();
            var lineIndex = line.LineNumber - 1;
            if (lineIndex < 0 || lineIndex >= lineKinds.Count)
            {
                return;
            }

            ChangeLinePart(line.Offset, line.EndOffset, element =>
            {
                switch (lineKinds[lineIndex])
                {
                    case DiffLineKind.Added:
                        element.TextRunProperties.SetForegroundBrush(new SolidColorBrush(Color.FromRgb(0x1B, 0x5E, 0x20)));
                        element.TextRunProperties.SetBackgroundBrush(new SolidColorBrush(Color.FromRgb(0xD9, 0xF2, 0xD9)));
                        break;
                    case DiffLineKind.Removed:
                        element.TextRunProperties.SetForegroundBrush(new SolidColorBrush(Color.FromRgb(0xB7, 0x1C, 0x1C)));
                        element.TextRunProperties.SetBackgroundBrush(new SolidColorBrush(Color.FromRgb(0xFF, 0xD6, 0xD6)));
                        element.TextRunProperties.SetTextDecorations(TextDecorations.Strikethrough);
                        break;
                }
            });
        }
    }
}

using System.Text.Json.Serialization;
using T3.Serialization;

namespace T3.Editor.UiModel.Selection;

public class TourPoint
{
    public string Title = string.Empty;
    public Guid Id { get; internal init; }
    public Guid ChildId;
    
    [JsonConverter(typeof(SafeEnumConverter<Styles>))]
    public Styles Style = Styles.Comment;

    public enum Styles
    {
        Comment,
        TourPoint,
        Tip,
    }

    internal TourPoint Clone()
    {
        return new TourPoint
                   {
                       Id = Guid.NewGuid(),
                       Title = Title,
                       Style = Style,
                   };
    }
}

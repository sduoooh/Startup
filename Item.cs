namespace startup
{
    public enum ItemMode
    {
        raw = 0,
        proxy = 1
    }
    public enum ControlLabel
    {
        sfw = 0,
        nsfw = 1
    }
    public abstract class StartUpItemConfigure
    {
        public string Name;
        public string Description;
        public ControlLabel Label;
        public bool Starred;
        public int Count;

        public abstract ItemMode Mode { get; }
    }

    public class RawConfigure : StartUpItemConfigure
    {
        public override ItemMode Mode => ItemMode.raw;

        public bool Waitable;
        public bool Associatable;
        public bool Executable;
        public string ExecutePath;
        public string SourcePath;

        public string PresetInput;
    }
}

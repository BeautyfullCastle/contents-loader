namespace H.ContentsLoader
{
    public enum ContentLife
    {
        /// <summary>The asset is released after the owning scene unloads.</summary>
        Scene = 0,

        /// <summary>The asset stays loaded until the application quits.</summary>
        App = 1,
    }
}

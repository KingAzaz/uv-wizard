#nullable enable

namespace UVWizard.Core.Packing
{
    internal readonly struct PackSettings
    {
        /// <param name="atlasSize">The atlas's width and height, in pixels.</param>
        /// <param name="padding">The least gap, in pixels, kept between any two charts.</param>
        /// <param name="blockSize">
        /// The packing grid's cell size, in pixels. 4 matches the BC and ASTC 4x4 blocks.
        /// </param>
        public PackSettings(int atlasSize, int padding, int blockSize = 4)
        {
            AtlasSize = atlasSize;
            Padding = padding;
            BlockSize = blockSize;
        }

        public int AtlasSize { get; }
        public int Padding { get; }
        public int BlockSize { get; }
    }
}

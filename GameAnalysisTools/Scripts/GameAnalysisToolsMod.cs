using System.IO;
using System.Text;
using PugMod;
using PugTilemap;
using PugTilemap.Quads;
using PugTilemap.Workshop;
using UnityEngine;

public sealed class GameAnalysisToolsMod : IMod
{
    private const string LogPrefix = "[GameAnalysisTools]";
    private const string OutputDirectoryName = "GameAnalysisTools";

    private bool _dumpCompleted;
    private int _attemptCount;

    public void EarlyInit()
    {
        Debug.Log($"{LogPrefix} Loaded.");
    }

    public void Init()
    {
    }

    public void Shutdown()
    {
    }

    public void ModObjectLoaded(Object obj)
    {
    }

    public void Update()
    {
        if (_dumpCompleted)
        {
            return;
        }

        _attemptCount++;
        if (!TilesetLutDumpUtility.TryCreateDump(out string outputPath, out string waitReason))
        {
            if (_attemptCount == 1 || _attemptCount % 120 == 0)
            {
                Debug.Log($"{LogPrefix} Waiting to dump tileset LUT. Attempt {_attemptCount}. {waitReason}");
            }

            return;
        }

        _dumpCompleted = true;
        Debug.Log($"{LogPrefix} Wrote tileset LUT dump to {outputPath}");
    }

    private static class TilesetLutDumpUtility
    {
        private const Tileset TargetTileset = Tileset.BaseBuildingUnpainted;
        private const LayerName TargetLayer = LayerName.litFloor;
        private const int TargetState = 0;
        private const bool DumpAllStates = false;
        private const bool DumpEmptyMasks = false;

        public static bool TryCreateDump(out string outputPath, out string waitReason)
        {
            outputPath = null;
            waitReason = null;

            PugMapTileset tileset = TilesetTypeUtility.GetTileset((int)TargetTileset);
            if (tileset == null)
            {
                waitReason = $"Tileset '{TargetTileset}' was not available yet.";
                return false;
            }

            QuadGenerator generator = tileset.GetDef(TargetLayer);
            if (generator == null)
            {
                waitReason = $"Layer '{TargetLayer}' was not found in tileset '{TargetTileset}'.";
                return false;
            }

            Texture2D texture = GetTargetTexture();
            if (texture == null)
            {
                waitReason = $"Texture for '{TargetTileset}/{TargetLayer}' was not available yet.";
                return false;
            }

            string directoryPath = Path.Combine(Application.persistentDataPath, OutputDirectoryName);
            Directory.CreateDirectory(directoryPath);

            string fileName = $"tileset_lut_{TargetTileset}_{TargetLayer}.txt";
            outputPath = Path.Combine(directoryPath, fileName);

            string dump = BuildDump(generator, texture);
            File.WriteAllText(outputPath, dump, Encoding.UTF8);
            return true;
        }

        private static Texture2D GetTargetTexture()
        {
            if (TilesetTypeUtility.GetAdaptiveTexture((int)TargetTileset, TargetLayer, TextureType.REGULAR) is Texture2D adaptiveTexture)
            {
                return adaptiveTexture;
            }

            if (TilesetTypeUtility.GetTexture((int)TargetTileset, TargetLayer, TextureType.REGULAR) is Texture2D texture)
            {
                return texture;
            }

            return null;
        }

        private static string BuildDump(QuadGenerator generator, Texture2D texture)
        {
            StringBuilder sb = new StringBuilder(16 * 1024);
            sb.AppendLine($"{LogPrefix} Tileset LUT dump");
            sb.AppendLine($"target_tileset={TargetTileset}");
            sb.AppendLine($"target_layer={TargetLayer}");
            sb.AppendLine($"mesh_fill_type={generator.meshFillType}");
            sb.AppendLine($"is_full_adaptive={generator.isUsingFullAdaptiveTexture}");
            sb.AppendLine($"texture={texture.name}");
            sb.AppendLine($"texture_size={texture.width}x{texture.height}");
            sb.AppendLine();

            int stateStart = DumpAllStates ? 0 : TargetState;
            int stateEnd = DumpAllStates ? 2 : TargetState;

            for (int state = stateStart; state <= stateEnd; state++)
            {
                sb.AppendLine($"[state {state}]");

                switch (generator.meshFillType)
                {
                    case QuadGenerator.FillType.AdaptativeFill:
                    case QuadGenerator.FillType.AdaptativeExtrude:
                        DumpAdaptiveState(sb, generator, texture, state);
                        break;
                    case QuadGenerator.FillType.RandomFill:
                    case QuadGenerator.FillType.RandomFillEditorOnly:
                    case QuadGenerator.FillType.CustomFill:
                        DumpDirectState(sb, generator, texture, state);
                        break;
                    default:
                        sb.AppendLine($"unsupported_mesh_fill_type={generator.meshFillType}");
                        break;
                }

                sb.AppendLine();
            }

            return sb.ToString();
        }

        private static void DumpAdaptiveState(StringBuilder sb, QuadGenerator generator, Texture2D texture, int state)
        {
            AdaptativeSpriteLookupTable lut = generator.isUsingFullAdaptiveTexture
                ? generator.generatedTextureAdaptativeSpriteLookupTable[state]
                : generator.adaptativeSpriteLookupTable[state];

            if (lut == null || lut.sublistLength == null || lut.sublistStart == null || lut.allSpriteCoords == null)
            {
                sb.AppendLine("lut=<null>");
                return;
            }

            for (int mask = 0; mask < 256; mask++)
            {
                int length = lut.sublistLength[mask];
                if (!DumpEmptyMasks && length == 0)
                {
                    continue;
                }

                sb.Append($"mask={mask}");
                sb.Append($" length={length}");

                if (length == 0)
                {
                    sb.AppendLine();
                    continue;
                }

                int start = lut.sublistStart[mask];
                for (int i = 0; i < length; i++)
                {
                    AppendRect(sb, texture, lut.allSpriteCoords[start + i], i);
                }

                sb.AppendLine();
            }
        }

        private static void DumpDirectState(StringBuilder sb, QuadGenerator generator, Texture2D texture, int state)
        {
            if (generator.allSpriteUVs == null || generator.allSpriteUVs.Length <= state || generator.allSpriteUVs[state] == null)
            {
                sb.AppendLine("direct_uvs=<null>");
                return;
            }

            for (int i = 0; i < generator.allSpriteUVs[state].spriteUVS.Count; i++)
            {
                sb.Append($"index={i}");
                AppendRect(sb, texture, generator.allSpriteUVs[state].spriteUVS[i], i);
                sb.AppendLine();
            }
        }

        private static void AppendRect(StringBuilder sb, Texture2D texture, Rect uvRect, int index)
        {
            int x = Mathf.RoundToInt(uvRect.x * texture.width);
            int yBottom = Mathf.RoundToInt(uvRect.y * texture.height);
            int width = Mathf.RoundToInt(uvRect.width * texture.width);
            int height = Mathf.RoundToInt(uvRect.height * texture.height);
            int yTop = texture.height - yBottom - height;

            sb.Append($" rect[{index}]");
            sb.Append($" bottom_left=({x},{yBottom},{width},{height})");
            sb.Append($" top_left=({x},{yTop},{width},{height})");
        }
    }
}

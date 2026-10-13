namespace CMAClothingFix;

internal static class ClothingOrder
{
    internal enum Kind { Unknown, LowerUnderwear, UpperUnderwear, Lower, Upper, Shoes, Jacket, Swimsuit,
        Socks, Accessories, Glasses, UpperUnderwearAccessory, LowerUnderwearAccessory, Gloves, Hat, FullBody, SleevedTop }

    // Native category/coverage values from the installed game's interop metadata; never infer from garment names.
    internal static Kind Classify(int type, int regions)
    {
        var kind = type switch
        {
            1 => Kind.LowerUnderwear, 2 => Kind.UpperUnderwear, 3 => Kind.Lower, 4 => Kind.Upper,
            5 => Kind.Shoes, 6 => Kind.Jacket, 7 => Kind.Swimsuit, 8 => Kind.Socks,
            9 => Kind.Accessories, 10 => Kind.Glasses, 11 => Kind.UpperUnderwearAccessory,
            12 => Kind.LowerUnderwearAccessory, 13 => Kind.Gloves, 14 => Kind.Hat, _ => Kind.Unknown
        };
        if (kind is not (Kind.Unknown or Kind.Upper or Kind.Lower)) return kind;
        bool torso = (regions & 2097152) != 0;
        if (torso && (regions & (256 | 512)) != 0) return Kind.SleevedTop;
        if (torso && (regions & (4 | 16 | 32768)) != 0) return Kind.FullBody;
        if (kind == Kind.Unknown && torso) return Kind.Upper;
        if (kind == Kind.Unknown && (regions & (4 | 16 | 32768)) != 0) return Kind.Lower;
        return kind;
    }

    private static int Rank(Kind kind) => kind switch
    {
        Kind.UpperUnderwearAccessory or Kind.LowerUnderwearAccessory => 0,
        Kind.UpperUnderwear or Kind.LowerUnderwear or Kind.Socks => 10,
        Kind.Swimsuit => 15,
        Kind.Upper or Kind.Lower or Kind.Shoes or Kind.Accessories or Kind.Glasses or Kind.Gloves or Kind.Hat => 20,
        Kind.FullBody => 30, Kind.SleevedTop => 35, Kind.Jacket => 40, _ => -1
    };

    internal static bool WaistSeam(int targetLayer, Kind targetKind, int outerLayer, Kind outerKind) =>
        targetLayer >= 0 && targetLayer == outerLayer && targetKind == Kind.Lower
        && outerKind is Kind.Upper or Kind.SleevedTop;

    internal static bool Covers(int targetLayer, int targetRegions, Kind targetKind, int outerLayer, int outerRegions, Kind outerKind)
    {
        // Some body jewellery uses accessory layer 4 and no flags, despite its explicit underwear-accessory category.
        int Layer(int layer, Kind kind) => layer < 0 ? layer : kind is Kind.UpperUnderwearAccessory or Kind.LowerUnderwearAccessory ? Math.Min(layer,1) : layer;
        int Regions(int regions, Kind kind) => regions != 0 ? regions : kind switch
        {
            Kind.UpperUnderwearAccessory => 1 | 2,
            Kind.LowerUnderwearAccessory => 4 | 8 | 16 | 32 | 32768,
            _ => 0
        };
        targetLayer = Layer(targetLayer,targetKind); outerLayer = Layer(outerLayer,outerKind);
        targetRegions = Regions(targetRegions,targetKind); outerRegions = Regions(outerRegions,outerKind);
        if (targetLayer < 0 || outerLayer < 0 || targetRegions == 0 || (outerRegions & targetRegions) == 0) return false;
        if (outerLayer != targetLayer) return outerLayer > targetLayer;
        // Tops may be tucked into trousers. Sleeve coverage does not make their hem an outer layer.
        bool Top(Kind kind) => kind is Kind.Upper or Kind.SleevedTop;
        if (Top(targetKind) && outerKind == Kind.Lower || Top(outerKind) && targetKind == Kind.Lower) return false;
        int targetRank = Rank(targetKind), outerRank = Rank(outerKind);
        if ((targetRank < 0) != (outerRank < 0)) return false;
        if (targetRank >= 0 && outerRank >= 0 && targetRank != outerRank) return outerRank > targetRank;
        // Equal/unknown categories are ordered only by strict containment; equal coverage stays ambiguous.
        return targetRegions != outerRegions && (outerRegions & targetRegions) == targetRegions;
    }
}

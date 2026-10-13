using CMAClothingFix;

internal static class OrderChecks
{
    internal static void Run()
    {
        const int torso = 2097152, pelvis = 4 | 16 | 32768, arms = 256 | 512, legs = 64 | 128, feet = 8192;
        var examples = new (string name, int type, int flags, ClothingOrder.Kind kind)[]
        {
            ("briefs",1,pelvis,ClothingOrder.Kind.LowerUnderwear), ("bra",2,3,ClothingOrder.Kind.UpperUnderwear),
            ("trousers",3,pelvis|legs,ClothingOrder.Kind.Lower), ("top",4,torso|3,ClothingOrder.Kind.Upper),
            ("shoes",5,feet,ClothingOrder.Kind.Shoes), ("jacket",6,torso|arms|3,ClothingOrder.Kind.Jacket),
            ("swimsuit",7,torso|pelvis|3,ClothingOrder.Kind.Swimsuit), ("socks",8,legs|feet,ClothingOrder.Kind.Socks),
            ("accessory",9,4194304,ClothingOrder.Kind.Accessories), ("glasses",10,131072,ClothingOrder.Kind.Glasses),
            ("upper underwear accessory",11,3,ClothingOrder.Kind.UpperUnderwearAccessory),
            ("lower underwear accessory",12,pelvis,ClothingOrder.Kind.LowerUnderwearAccessory),
            ("gloves",13,16384,ClothingOrder.Kind.Gloves), ("hat",14,16777216,ClothingOrder.Kind.Hat),
            ("dress",4,torso|pelvis|3,ClothingOrder.Kind.FullBody),
            ("hoodie",4,torso|arms|3,ClothingOrder.Kind.SleevedTop), ("unknown",99,0,ClothingOrder.Kind.Unknown)
        };
        int checks = 0;
        void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
        foreach (var item in examples) Check(ClothingOrder.Classify(item.type,item.flags)==item.kind,item.name+" classification");
        Check(ClothingOrder.Classify(0,torso|pelvis)==ClothingOrder.Kind.FullBody,"native uncatalogued dress uses coverage");
        Check(ClothingOrder.Classify(0,pelvis|legs)==ClothingOrder.Kind.Lower,"native uncatalogued trousers use coverage");
        Check(ClothingOrder.Classify(0,131072)==ClothingOrder.Kind.Unknown,"unknown face accessory is not guessed");
        bool Covers(int target, int outer, int targetLayer=3, int outerLayer=3) => ClothingOrder.Covers(targetLayer,
            examples[target].flags,examples[target].kind,outerLayer,examples[outer].flags,examples[outer].kind);
        Check(Covers(2,14),"dress over trousers"); Check(Covers(14,15),"hoodie over dress");
        Check(Covers(15,5),"jacket over hoodie"); Check(Covers(1,3),"top over bra");
        const int shirtFlags = 1 | 2 | 256 | 512 | 262144 | 524288 | 2097152 | 8388608;
        const int pantsFlags = 4 | 8 | 16 | 32 | 64 | 128 | 32768 | 524288;
        var shirtKind = ClothingOrder.Classify(0,shirtFlags);
        var pantsKind = ClothingOrder.Classify(0,pantsFlags);
        Check(!ClothingOrder.Covers(3,pantsFlags,pantsKind,3,shirtFlags,shirtKind),"native tucked shirt cannot cut trouser waistband or belt");
        Check(!ClothingOrder.Covers(3,shirtFlags,shirtKind,3,pantsFlags,pantsKind),"same-layer trousers cannot cut a native shirt hem");
        Check(ClothingOrder.WaistSeam(3,pantsKind,3,shirtKind),"shirt/trouser correction is restricted to a waist seam");
        Check(!ClothingOrder.WaistSeam(3,shirtKind,3,pantsKind),"waist seam cannot trim the shirt");
        Check(Covers(0,2),"trousers over briefs"); Check(Covers(6,14),"dress over swimsuit");
        Check(Covers(7,4),"shoes over socks"); Check(Covers(10,1),"bra over underwear accessory");
        Check(Covers(11,0),"briefs over underwear accessory");
        foreach (var a in Enumerable.Range(0,examples.Length)) foreach (var b in Enumerable.Range(0,examples.Length))
        {
            Check(!(Covers(a,b)&&Covers(b,a)),"mutual masking "+examples[a].name+" / "+examples[b].name);
            bool underwearAccessory = examples[a].kind is ClothingOrder.Kind.UpperUnderwearAccessory or ClothingOrder.Kind.LowerUnderwearAccessory
                || examples[b].kind is ClothingOrder.Kind.UpperUnderwearAccessory or ClothingOrder.Kind.LowerUnderwearAccessory;
            if (!underwearAccessory) Check(!Covers(a,b,4,3),"native lower layer cannot cover a higher layer");
            for (int targetLayer=0; targetLayer<8; targetLayer++) for (int outerLayer=0; outerLayer<8; outerLayer++)
                if (!underwearAccessory && targetLayer != outerLayer) Check(Covers(a,b,targetLayer,outerLayer)
                    == (outerLayer>targetLayer && examples[a].flags!=0 && (examples[a].flags & examples[b].flags)!=0),
                    "all eight native layer levels retain precedence");
            if ((examples[a].flags & examples[b].flags)==0) Check(!Covers(a,b),"disjoint zones remain visible");
            foreach (var c in Enumerable.Range(0,examples.Length))
                Check(!(Covers(a,b)&&Covers(b,c)&&Covers(c,a)),"three garment cycle");
        }
        Check(!ClothingOrder.Covers(-1,3,ClothingOrder.Kind.Upper,3,3,ClothingOrder.Kind.Jacket),"unknown native layer");
        Check(ClothingOrder.Covers(4,0,ClothingOrder.Kind.UpperUnderwearAccessory,3,torso|3,ClothingOrder.Kind.FullBody),"zero-flag piercing at layer 4 is covered by clothing");
        Check(!ClothingOrder.Covers(3,torso|3,ClothingOrder.Kind.FullBody,4,0,ClothingOrder.Kind.UpperUnderwearAccessory),"piercing cannot cut holes into a dress");
        Check(!ClothingOrder.Covers(5,0,ClothingOrder.Kind.Accessories,3,torso|3,ClothingOrder.Kind.FullBody),"unclassified bracelet remains untouched");
        Check(!ClothingOrder.Covers(3,3,ClothingOrder.Kind.Upper,3,3,ClothingOrder.Kind.Upper),"equivalent tops stay ambiguous");
        Console.WriteLine($"PASS {checks} clothing order checks across all 14 native categories, full-body and sleeved variants.");
    }
}

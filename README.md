# CMA Clothing Fix 0.5.1

0.5.0 boundary subdivision and waist-band correction have been removed after visual corruption and stutters. Runtime masking retains original vertices, UVs, skin weights and blend shapes and changes triangle indices only. Garment layers, piercing correction and opaque/alpha surface checks remain. Wardrobe polling runs every 300 ms; pose masks update automatically. / opens the English menu.

Partially covered triangles are retained. Fine boundary clipping is unresolved. Visual stability and frame pacing after this regression fix still require an in-game comparison.

Build: dotnet build -c Release. Managed checks: dotnet run --project tests/GeometryChecks.csproj -c Release. Pure refinement experiments are not active in the installed runtime.

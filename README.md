# KevCodeLensFilter

This Visual Studio VSIX contributes a separate **Filtered References** Code Lens for C# methods and types. It uses Roslyn to find semantic references and omits references found inside NUnit, MSTest, and xUnit test methods. MSTest/NUnit test fixture classes and common setup/cleanup methods are filtered as well.

The new lens does not modify Visual Studio's built-in References Code Lens. In Tools > Options > Text Editor > CodeLens, enable **Show Filtered References**; disable the built-in References indicator if you want only the filtered count. The custom indicator appears alongside other CodeLens indicators above C# methods and types, with a label such as `3 non-test references`.

The extension uses Visual Studio's in-process Roslyn workspace through a Code Lens callback. Build the solution to produce the VSIX package, then install the `.vsix` file in the Visual Studio instance where you want to use it. If debugging with the **Visual Studio Experimental Instance** launch profile, install the VSIX into that experimental hive as well; installing it in the regular Visual Studio instance does not install it into the experimental instance.

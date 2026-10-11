using System.Runtime.CompilerServices;

// The Avalonia head (assembly name "GHelper") reads the localized strings (internal class GHelper.Properties.Strings);
// its test project asserts on them too.
[assembly: InternalsVisibleTo("GHelper")]
[assembly: InternalsVisibleTo("GHelper.App.Tests")]

using System.Text;
using XbfKit;

if (args.Length != 2)
{
    Console.Error.WriteLine("Usage: XbfCompiler <input.xaml> <output.xbf>");
    return 2;
}

string xamlPath = Path.GetFullPath(args[0]);
string xbfPath = Path.GetFullPath(args[1]);
string xaml = File.ReadAllText(xamlPath);
var options = new XbfCompilationOptions
{
    Version = XbfVersion.Version2_1,
    Dialect = XbfDialect.MUX
};

Directory.CreateDirectory(Path.GetDirectoryName(xbfPath)!);
using (FileStream output = File.Create(xbfPath))
{
    XbfCompiler.Compile(xaml, output, options);
}

Console.WriteLine($"XBF_VERSION={options.Version}");
Console.WriteLine($"XBF_DIALECT={options.Dialect}");
Console.WriteLine($"XAML_BYTES={Encoding.UTF8.GetByteCount(xaml)}");
Console.WriteLine($"XBF_BYTES={new FileInfo(xbfPath).Length}");
Console.WriteLine($"OUTPUT={xbfPath}");
return 0;

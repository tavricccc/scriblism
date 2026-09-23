namespace Scriblism.Core;

public sealed record Language(string Id, string Name, string Extensions, string Keywords, string LineComment = "//", bool BlockComment = true);

public static class Languages
{
    private const string C = "if else for while do switch case break continue return class struct enum public private protected static const new this true false null void int string bool float double long short char unsigned signed sizeof typedef using namespace try catch finally throw async await import export from default extends implements interface package function var let yield typeof instanceof delete in of get set abstract override virtual sealed readonly record required init out ref internal object base operator params checked unchecked partial delegate event volatile extern";
    public static IReadOnlyList<Language> All { get; } = [
        new("text", "純文字", ".txt .log", "", "", false),
        new("markdown", "Markdown", ".md .markdown .mdown", "", "", false),
        new("csharp", "C#", ".cs .csx", C),
        new("cpp", "C / C++", ".c .h .cpp .hpp .cc .cxx .hxx", C + " template typename constexpr nullptr auto include define pragma std union friend noexcept"),
        new("javascript", "JavaScript", ".js .mjs .cjs .jsx", C + " undefined constructor debugger"),
        new("typescript", "TypeScript", ".ts .tsx .mts .cts", C + " type declare keyof infer as unknown never any number boolean satisfies"),
        new("python", "Python", ".py .pyw .pyi", "False None True and as assert async await break class continue def del elif else except finally for from global if import in is lambda nonlocal not or pass raise return try while with yield match case", "#", false),
        new("java", "Java", ".java", C + " synchronized transient throws super final native strictfp"),
        new("kotlin", "Kotlin", ".kt .kts", C + " fun val when is as data companion suspend inline lateinit actual expect"),
        new("swift", "Swift", ".swift", C + " func protocol extension guard defer nil mutating associatedtype some actor weak"),
        new("go", "Go", ".go", C + " func chan defer go map range select fallthrough nil type"),
        new("rust", "Rust", ".rs", C + " fn pub mod impl trait mut self Self match loop move crate super use where unsafe dyn as macro_rules"),
        new("ruby", "Ruby", ".rb .rake .gemspec", "def end class module begin rescue ensure require include attr_reader attr_accessor nil true false if else elsif unless while until do for in return yield self super then and or not alias next redo retry raise", "#", false),
        new("php", "PHP", ".php .phtml", C + " echo print require require_once include include_once foreach elseif endif as fn trait use"),
        new("powershell", "PowerShell", ".ps1 .psm1 .psd1", "function param begin process end if else elseif foreach for while do until switch break continue return try catch finally throw trap filter class enum using in true false null script global local", "#", false),
        new("shell", "Shell", ".sh .bash .zsh .fish", "if then else elif fi for in do done while until case esac function select time export local readonly return break continue source echo set unset trap", "#", false),
        new("sql", "SQL", ".sql", "select from where join left right inner outer full on as insert into values update set delete create alter drop table index view database distinct group by order having asc desc limit offset union all exists null not and or in like between case when then else end primary key references constraint with recursive returning true false count sum avg min max", "--"),
        new("html", "HTML", ".html .htm .vue .svelte .astro", "", "", false),
        new("xml", "XML / XAML", ".xml .xaml .csproj .props .targets .slnx .svg .resx .config .manifest", "", "", false),
        new("css", "CSS / SCSS", ".css .scss .sass .less", "important inherit initial unset auto none block flex grid relative absolute fixed sticky transparent repeat minmax var calc media supports import keyframes", "//"),
        new("json", "JSON", ".json .jsonc .ipynb", "true false null"),
        new("yaml", "YAML", ".yaml .yml", "true false null yes no on off", "#", false),
        new("toml", "TOML / INI", ".toml .ini .cfg .editorconfig", "true false", "#", false),
        new("lua", "Lua", ".lua", "and break do else elseif end false for function goto if in local nil not or repeat return then true until while", "--", false),
        new("r", "R", ".r .rprofile", "if else repeat while function for in next break TRUE FALSE NULL Inf NaN NA library require", "#", false),
        new("dart", "Dart", ".dart", C + " dynamic final factory mixin required late covariant extension with"),
        new("scala", "Scala", ".scala .sc", C + " def val object trait implicit lazy match with given then end"),
        new("fsharp", "F#", ".fs .fsx .fsi", "let rec in fun function match with type module namespace open if then else elif for do done while yield return async member override abstract interface inherit mutable true false null None Some"),
        new("dockerfile", "Dockerfile", "dockerfile", "FROM RUN CMD LABEL EXPOSE ENV ADD COPY ENTRYPOINT VOLUME USER WORKDIR ARG ONBUILD STOPSIGNAL HEALTHCHECK SHELL", "#", false),
        new("make", "Makefile", "makefile .mk", "include ifdef ifndef ifeq ifneq else endif define endef export unexport override private vpath", "#", false),
        new("batch", "Batch", ".bat .cmd", "echo off on set if else for in do goto call exit rem pause shift not exist errorlevel defined", "rem ", false),
        new("diff", "Diff / Patch", ".diff .patch", "", "", false),
        new("graphql", "GraphQL", ".graphql .gql", "query mutation subscription fragment on schema type input enum interface union scalar extend implements directive true false null", "#", false)
    ];

    public static Language Detect(string? path)
    {
        if (path is null) return All[0];
        var filename = Path.GetFileName(path).ToLowerInvariant();
        if (filename.StartsWith("dockerfile", StringComparison.Ordinal)) return ById("dockerfile");
        if (filename is "makefile" or "gnumakefile") return ById("make");
        if (filename is ".gitignore" or ".gitattributes" or ".env") return ById("shell");
        var extension = Path.GetExtension(filename);
        return All.FirstOrDefault(l => l.Extensions.Split(' ').Contains(extension.Length == 0 ? filename : extension, StringComparer.OrdinalIgnoreCase)) ?? All[0];
    }
    public static Language ById(string id) => All.FirstOrDefault(l => l.Id == id) ?? All[0];
}

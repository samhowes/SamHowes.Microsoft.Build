using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Build.Construction;
using Microsoft.Build.Evaluation;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Editing;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Text;
using Project = Microsoft.CodeAnalysis.Project;

namespace Converter
{
    class Program
    {
        static async Task Main(string[] args)
        {
            string? currentDirectory = Directory.GetCurrentDirectory();
            while (true)
            {
                if (Directory.Exists(Path.Combine(currentDirectory, ".git"))) break;
                currentDirectory = Path.GetDirectoryName(currentDirectory);
                if (currentDirectory == null)
                {
                    Console.Error.WriteLine($"Failed to find repository root.");
                    return;
                }
            }

            var repoRoot = currentDirectory!;
            var sourceRoot = Path.GetFullPath(Path.Combine(repoRoot, "..", "msbuild"));
            var processor = new Processor(repoRoot, sourceRoot, new Files());
            processor.Publicize();
            processor.SetPackageId();
            await processor.UpdateTranslator();
            await processor.UpdateBuildManager();
            await processor.UpdateResultsCache();
            processor.SetVersion("16.9.0");
        }
    }

    public class Files
    {
        public virtual string GetContents(string path) => File.ReadAllText(path);
        public virtual void WriteContents(string path, string contents) => File.WriteAllText(path, contents);
        public virtual IEnumerable<string> GetFiles(string path) => Directory.EnumerateFiles(path);
        public virtual IEnumerable<string> GetDirectories(string path) => Directory.EnumerateDirectories(path);
    }
    
    public class Processor
    {
        private readonly string _repoRoot;
        private readonly string _msbuildRoot;
        private readonly string _srcRoot;
        private readonly string _sharedRoot;
        private readonly string _frameworkRoot;
        private readonly Files _files;

        public Processor(string repoRoot, string msbuildRoot, Files files)
        {
            _repoRoot = repoRoot;
            _msbuildRoot = msbuildRoot;
            _files = files;
            _srcRoot = Path.Combine(msbuildRoot, "src");
            _sharedRoot = Path.Combine(_srcRoot, "Shared");
            _frameworkRoot = Path.Combine(_srcRoot, "Build");
        }

        public void Publicize()
        {
            var targetDirectories = new[] {_frameworkRoot, _sharedRoot};

            bool replaced = false;
            string MakePublic(Match match)
            {
                replaced = true;
                var res = new StringBuilder();
                res.Append(match.Groups["indent"].Value);
                
                
                // don't return `public <PropertyName> {get; public set}`
                var accessor = match.Groups["accessor"];
                if (accessor.Success)
                {
                    res.Append(accessor.Value);
                }
                else
                {
                    
                    var modifier = match.Groups["modifier"];
                    // don't return `protected public`
                    if (modifier.Success && modifier.Value.Trim() != "protected")
                        res.Append(modifier);
                    
                    res.Append("public");
                    res.Append(match.Groups["suffix"]);
                }

                return res.ToString();
            }

            string SearchForPublic(Match match)
            {
                replaced = true;
                return match.Value + $" | {match.Groups["qualifier"]}BindingFlags.Public";
            }
            
            var flags = RegexOptions.Compiled | RegexOptions.Multiline | RegexOptions.IgnoreCase;
            var regexes = new []
            {
                (new Regex(@"^(?<indent>\s+)(?<modifier>(static|sealed|abstract|protected)\s)?internal(?<suffix>\s)(?<accessor>set)?", flags),
                    (MatchEvaluator) MakePublic),
                (new Regex(@"(?<qualifier>System.Reflection.)?BindingFlags.NonPublic", flags), (MatchEvaluator) SearchForPublic)
            };

            void Walk(string directory)
            {
                foreach (var subDirectory in _files.GetDirectories(directory))
                    Walk(subDirectory);
                foreach (var file in _files.GetFiles(directory))
                {
                    if (!file.EndsWith(".cs")) continue;
                    var contents = _files.GetContents(file);
                    replaced = false;
                    foreach (var (regex, evaluator) in regexes!)
                    {
                        contents = regex.Replace(contents, evaluator);
                    }
                    if (replaced)
                        _files.WriteContents(file, contents);
                }
            }

            foreach (var directory in targetDirectories)       
                Walk(directory);
        }

        public void SetPackageId()
        {
            var xml = ProjectRootElement.Open(Path.Combine(_frameworkRoot, "Microsoft.Build.csproj"), 
                ProjectCollection.GlobalProjectCollection, 
                preserveFormatting:true)!;
            var group = xml.PropertyGroups.First();

            ProjectPropertyElement GetOrAddProperty(string name)
            {
                var property = xml!.Properties.FirstOrDefault(p => p.Name == name);
                if (property == null)
                {
                    property = xml.CreatePropertyElement("PackageId");
                    group!.AppendChild(property);                    
                }
                return property;
            }
            
            var noWarn = GetOrAddProperty("NoWarn");
            // warnings for naming things, CLS-compliance, and parameters in xml comments
            noWarn.Value = $"{noWarn.Value};CS3001;CS3002;CS3003;CS3005;CS3008;CS1573";
            GetOrAddProperty("PackageId").Value = "SamHowes.Microsoft.Build";
            xml.Save();
        }
        
        public async Task UpdateTranslator()
        {
            var _ = typeof(Microsoft.CodeAnalysis.CSharp.Formatting.CSharpFormattingOptions);
            
            var wrapper = new Editor();
            await CustomizeReader(wrapper);
            await CustomizeTranslator(wrapper);
        }

        private async Task CustomizeTranslator(Editor wrapper)
        {
            var path = Path.Combine(_sharedRoot, "BinaryTranslator.cs");
            var (root, editor) = await wrapper.LoadDocument(path);

            var classes = root.DescendantNodes()
                .OfType<ClassDeclarationSyntax>().First()
                .DescendantNodes().OfType<ClassDeclarationSyntax>()
                .ToList();

            var cls = classes[1];

            editor.SetAccessibility(cls, Accessibility.Public);
            var ctor = cls.ChildNodes().OfType<ConstructorDeclarationSyntax>().First();
            var props = new SyntaxNode[]
            {
                SyntaxFactory.PropertyDeclaration(
                        SyntaxFactory.ParseTypeName("Func<Stream, BinaryWriter>"), "BinaryWriterFactory")
                    .AddModifiers(
                        SyntaxFactory.Token(SyntaxKind.PublicKeyword), 
                        SyntaxFactory.Token(SyntaxKind.StaticKeyword))
                    .AddAccessorListAccessors(
                        SyntaxFactory.AccessorDeclaration(SyntaxKind.GetAccessorDeclaration)
                            .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken)),
                        SyntaxFactory.AccessorDeclaration(SyntaxKind.SetAccessorDeclaration)
                            .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken)))
                    .NormalizeWhitespace(indentation: "    ", eol: "\n")
            };
            
            editor.InsertBefore(ctor, props);

            var param = ctor.ParameterList.Parameters.First();
            editor.ReplaceNode(ctor.Body!.Statements.Last(), (n, g) =>
            {
                var e = (ExpressionStatementSyntax) n;
                var ass = (AssignmentExpressionSyntax) e.Expression;
            
                StatementSyntax what = SyntaxFactory.ParseStatement(
                    $"{ass.Left.ToString()} = BinaryWriterFactory({param.Identifier.Value});");
            
                return what;
            });
            
            
            
            await Write(editor, path);
        }

        private async Task CustomizeReader(Editor wrapper)
        {
            var path = Path.Combine(_sharedRoot, "InterningBinaryReader.cs");
            var (root, editor) = await wrapper.LoadDocument(path);

            var cls = root!.DescendantNodes()
                .First(n => n is ClassDeclarationSyntax {Identifier: {Text: "InterningBinaryReader"}});

            foreach (var childClass in cls.DescendantNodes().OfType<ClassDeclarationSyntax>())
                editor.SetAccessibility(childClass, Accessibility.Public);

            var patch = CSharpSyntaxTree.ParseText(
                await File.ReadAllTextAsync(Path.Combine(_repoRoot, "InterningBinaryReader.patch.cs")));

            var patchType = (await patch.GetRootAsync())
                .DescendantNodes().OfType<InterfaceDeclarationSyntax>().First();


            var ctor = cls.ChildNodes().OfType<ConstructorDeclarationSyntax>().First();

            var props = new SyntaxNode[]
            {
                SyntaxFactory.PropertyDeclaration(
                        SyntaxFactory.ParseTypeName(patchType.Identifier.Text), "OpportunisticIntern")
                    .AddModifiers(
                        SyntaxFactory.Token(SyntaxKind.PublicKeyword), 
                        SyntaxFactory.Token(SyntaxKind.StaticKeyword))
                    .AddAccessorListAccessors(
                        SyntaxFactory.AccessorDeclaration(SyntaxKind.GetAccessorDeclaration)
                            .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken)),
                        SyntaxFactory.AccessorDeclaration(SyntaxKind.SetAccessorDeclaration)
                            .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken)))
                    .NormalizeWhitespace(indentation: "    ", eol: "\n")
            };
            editor.InsertBefore(ctor, props);
            editor.InsertAfter(cls, patchType);

            var create = cls.ChildNodes().OfType<MethodDeclarationSyntax>().Single(m => m.Identifier.Text == "Create");
            editor.SetType(create, SyntaxFactory.ParseTypeName("InterningBinaryReader"));

            await Write(editor, path);
        }

        private static async Task Write(DocumentEditor editor, string path)
        {
            var document = await Formatter.FormatAsync(editor.GetChangedDocument());
            // var text = editor.GetChangedRoot().ToFullString();
            var text = await document.GetTextAsync();
            await File.WriteAllTextAsync(path, text.ToString());
        }

        public void SetVersion(string version)
        {
            var path = Path.Combine(_msbuildRoot, "eng", "Versions.props");
            var versionsProps = ProjectRootElement.Open(path);

            var versionPrefix = versionsProps!.Properties.First(p => p.Name == "VersionPrefix");
            versionPrefix.Value = version;
            versionsProps.Save(path);
        }

        public async Task UpdateBuildManager()
        {
            var wrapper = new Editor();
            var path = Path.Combine(_frameworkRoot, "BackEnd/BuildManager/BuildManager.cs");
            var (root, editor) = await wrapper.LoadDocument(path);
        
            var cls = root!.DescendantNodes()
                .First(n => n is ClassDeclarationSyntax {Identifier: {Text: "BuildManager"}});

            foreach (var method in cls.DescendantNodes()
                .OfType<MethodDeclarationSyntax>()
                .Where(m => m.Identifier.Text == "GetNewConfigurationId"))
                editor.SetAccessibility(method, Accessibility.Public);

            var reuseOldCaches = cls.DescendantNodes()
                .OfType<MethodDeclarationSyntax>()
                .Single(m => m.Identifier.Text == "ReuseOldCaches");

            var newMethod = SyntaxFactory.ParseMemberDeclaration(@"
        public void ReuseOldCaches(IConfigCache configCache, IResultsCache resultsCache)
        {
            _componentFactories.ReplaceFactory(BuildComponentType.ConfigCache, configCache);
            _componentFactories.ReplaceFactory(BuildComponentType.ResultsCache, resultsCache);
        }
");
            editor.InsertAfter(reuseOldCaches, newMethod!);
            await Write(editor, path);
        }

        public async Task UpdateResultsCache()
        {
            // await UpdateResultsCacheOverride();
            
            // await UpdateCacheAggregator();
            foreach (var (shortPath, methodNames, fieldNames) in new[]
            {
                ("BackEnd/Components/Caching/ResultsCache.cs", new []{"ClearResultsForConfiguration"},
                    new string[]
                    {
                        // "_resultsByConfiguration"
                    }),
                // ("BackEnd/Shared/BuildResult.cs", new[] {("MergeResults"), "AddResultsForTarget"}, new []{"_resultsByTarget", "_requestException"})
            })
            {
                var wrapper = new Editor();
                var path = Path.Combine(_frameworkRoot, shortPath);
                var (root, editor) = await wrapper.LoadDocument(path);

                var className = Path.GetFileNameWithoutExtension(shortPath);
                var cls = root!.DescendantNodes()
                    .OfType<ClassDeclarationSyntax>()
                    .Single(c => c.Identifier.Text == className);

                foreach (var methodName in methodNames)
                {
                    var configurationId = cls.DescendantNodes()
                        .Single(n => n is MethodDeclarationSyntax m && m.Identifier.Text == methodName);
            
                    editor.SetModifiers(configurationId, DeclarationModifiers.Virtual);    
                }

                foreach (var fieldName in fieldNames)
                {
                    var resultsByTarget = cls.DescendantNodes()
                        .Single(c => c is FieldDeclarationSyntax f &&
                                     f.Declaration.Variables.Any(v => v.Identifier.Text == fieldName));
                    editor.SetAccessibility(resultsByTarget, Accessibility.Public);
                }
                
                await Write(editor, path);   
            }
        }

        private async Task UpdateCacheAggregator()
        {
            var path = Path.Combine(_frameworkRoot, "BackEnd/BuildManager/CacheAggregator.cs");

            // The cache aggregator operates on the same assumption, it will be true on the aggregate, but not on 
            // the individual caches.
            var regex = new Regex(@"^[ ]+ErrorUtilities.VerifyThrow.*Assuming 1-to-1 mapping between configs and results",
                RegexOptions.IgnoreCase | RegexOptions.Multiline);

            var replaced = regex.Replace(await File.ReadAllTextAsync(path), "//$0");
            await File.WriteAllTextAsync(path, replaced);

            var wrapper = new Editor();
            var (root, editor) = await wrapper.LoadDocument(path);
            var insertCachesMethod = root.DescendantNodes()
                .Single(n => n is MethodDeclarationSyntax {Identifier: {Text: "InsertCaches"}});

            var variables = insertCachesMethod.DescendantNodes()
                .OfType<VariableDeclarationSyntax>()
                .ToList();

            var index = -1;
            for (var i = 0; i < variables.Count; i++)
            {
                var actual = variables[i];
                if (actual.Variables.All(v => v.Identifier.Text != "seenConfigIds")) continue;
                index = i;
                break;
            }

            if (index < 0) throw new Exception(":(");

            var seenConfigs = variables[index];
            var configIdMapping = variables[index + 1];

            var ctor = root.DescendantNodes()
                .OfType<ConstructorDeclarationSyntax>()
                .Single(c => c.Identifier.Text == "CacheAggregator");

            var lastBody = ctor.Body!.Statements.Last();
            foreach (var variableDeclaration in new[] {seenConfigs, configIdMapping})
            {
                editor.RemoveNode(variableDeclaration.Parent!);
                var variable = variableDeclaration.Variables.Single();
                var type = ((ObjectCreationExpressionSyntax) variable.Initializer!.Value).Type;
                var field = SyntaxFactory.ParseMemberDeclaration(
                    $"public static {type} {variable.Identifier.Text};\n");

                editor.InsertBefore(ctor, field!);

                var assignment = SyntaxFactory.ParseStatement(variable.GetText().ToString() + ";\n");

                editor.InsertAfter(lastBody, assignment);
            }

            await Write(editor, path);
        }

        private async Task UpdateResultsCacheOverride()
        {
            // this file does a verification of the overridden cache to make sure it doesn't overlap with the current
            // results, we're fine with this. This is only active in debug mode anyways
            var path = Path.Combine(_frameworkRoot, "BackEnd/Components/Caching/ResultsCacheWithOverride.cs");

            var regex = new Regex(@"^#if\s\w+\s+ErrorUtilities.VerifyThrow.*?caches.*?overlap.*?;\s+#endif",
                RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Multiline);

            var replaced = regex.Replace(await File.ReadAllTextAsync(path), "");
            await File.WriteAllTextAsync(path, replaced);
        }
    }

    public class Editor
    {
        private readonly AdhocWorkspace _workspace;
        private readonly Project _newProject;

        public Editor()
        {
            _workspace = new AdhocWorkspace();
            var projectId = ProjectId.CreateNewId();
            var versionStamp = VersionStamp.Create();
            var projectInfo = ProjectInfo.Create(projectId, versionStamp, "NewProject", "projName", LanguageNames.CSharp);
            _newProject = _workspace.AddProject(projectInfo);
            
        }

        public async Task<(SyntaxNode root, DocumentEditor editor)> LoadDocument(string path)
        {
            var sourceText = SourceText.From(await File.ReadAllTextAsync(path));
            var document = _workspace.AddDocument(_newProject.Id, "NewFile.cs", sourceText);
            var root = await document.GetSyntaxRootAsync()!;
            var editor = await DocumentEditor.CreateAsync(document);
            return (root!, editor);
        }
    }
}

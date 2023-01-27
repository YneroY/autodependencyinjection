using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace AutoDependencyInjection
{
    [Generator]
    public class AutoDependencyInjection : ISourceGenerator
    {
        public void Execute(GeneratorExecutionContext context)
        {
            Compilation compilation = context.Compilation;

            // Retreive the populated receiver 
            if (!(context.SyntaxReceiver is SyntaxReceiver receiver))
                return;

            // Do not proceed if no auto injection is required..
            if (receiver.CandidateFields["InjectAsSingleton"].Count < 1 &&
                receiver.CandidateFields["InjectAsTransient"].Count < 1 &&
                receiver.CandidateFields["InjectAsScoped"].Count < 1)
            {
                return;
            }

            // Function to add into file to create the class
            StringBuilder template = new StringBuilder();

            template.Append(@"
using System;
using Microsoft.Extensions.DependencyInjection;

");

            // Store injection
            Dictionary<string, Dictionary<string, string>> finalResultPairing = new Dictionary<string, Dictionary<string, string>>();

            // Store namespaces
            HashSet<string> namespaces = new HashSet<string>();

            foreach (var item in receiver.CandidateFields)
            {
                string injectionType = "";

                switch (item.Key)
                {
                    case "InjectAsSingleton":
                        injectionType = "Singleton";
                        break;

                    case "InjectAsTransient":
                        injectionType = "Transient";
                        break;

                    case "InjectAsScoped":
                        injectionType = "Scoped";
                        break;
                }

                if (string.IsNullOrEmpty(injectionType) || item.Value == null || item.Value.Count < 1)
                    continue;

                foreach (ClassDeclarationSyntax cds in item.Value)
                {
                    SemanticModel model = compilation.GetSemanticModel(cds.SyntaxTree);
                    //var root = cds.SyntaxTree.GetRoot();

                    INamedTypeSymbol classSymbol = model.GetDeclaredSymbol(cds) as INamedTypeSymbol;

                    // Add class namespace
                    namespaces.Add(classSymbol.ContainingNamespace.Name.ToString());

                    var implementedInterfaces = classSymbol.AllInterfaces;

                    if (implementedInterfaces.Count() == 1)
                    {
                        // Add interface namespace
                        namespaces.Add(implementedInterfaces[0].ContainingNamespace.Name.ToString());

                        if (finalResultPairing.ContainsKey(injectionType))
                        {
                            finalResultPairing[injectionType].Add(cds.Identifier.ValueText, implementedInterfaces[0].Name);
                        }
                        else
                        {
                            finalResultPairing.Add(injectionType, new Dictionary<string, string>() { { cds.Identifier.ValueText, implementedInterfaces[0].Name } });
                        }
                    }
                }
            }

            // Insert the namespaces
            foreach(string item in namespaces)
            {
                template.AppendLine($"using {item};");
            }

            // Insert the attributes required
            foreach (string item in namespaces)
            {
                template.AppendLine("");
                template.AppendLine(@"
namespace MyAutoInjector
{     
      [AttributeUsage(AttributeTargets.Class)]
      public class InjectAsSingleton : Attribute
      {}

      [AttributeUsage(AttributeTargets.Class)]
      public class InjectAsTransient : Attribute
      {}

      [AttributeUsage(AttributeTargets.Class)]
      public class InjectAsScoped : Attribute
      {}


");
            }

            if (finalResultPairing.Count > 0)
            {
                template.Append(@"

                public static class AutoInjector
                {
                    public static void AutoInject(this IServiceCollection services)
                    {
                ");

                foreach (var item in finalResultPairing)
                {
                    if (item.Key == "Singleton")
                    {
                        foreach (var record in item.Value)
                        {
                            template.AppendLine($"services.AddSingleton<{record.Value}, {record.Key}>();");
                        }
                    }
                    else if (item.Key == "Transient")
                    {
                        foreach (var record in item.Value)
                        {
                            template.AppendLine($"services.AddTransient<{record.Value}, {record.Key}>();");
                        }
                    }
                    else
                    {
                        foreach (var record in item.Value)
                        {
                            template.AppendLine($"services.AddScoped<{record.Value}, {record.Key}>();");
                        }
                    }
                }

                template.AppendLine(@"}");
                template.AppendLine(@"}");
            }

            template.AppendLine("}");

            context.AddSource($"AutoInjector.cs", SourceText.From(template.ToString(), Encoding.UTF8));
        }

        public void Initialize(GeneratorInitializationContext context)
        {
//#if DEBUG
//            if (!Debugger.IsAttached)
//            {
//                Debugger.Launch();
//            }
//#endif 

            // Register a syntax receiver that will be created for each generation pass.
            // In this case, to detect specific attribute param.
            context.RegisterForSyntaxNotifications(() => new SyntaxReceiver());
        }

        class SyntaxReceiver : ISyntaxReceiver
        {
            public Dictionary<string, List<ClassDeclarationSyntax>> CandidateFields { get; } = new Dictionary<string, List<ClassDeclarationSyntax>>()
            {
                { "InjectAsSingleton", new List<ClassDeclarationSyntax>() },
                { "InjectAsTransient", new List<ClassDeclarationSyntax>() },
                { "InjectAsScoped", new List<ClassDeclarationSyntax>() }
            };

            /// <summary>
            /// Called for every syntax node in the compilation, we can inspect the nodes and save any information useful for generation.
            /// </summary>
            public void OnVisitSyntaxNode(SyntaxNode syntaxNode)
            {
                // Only handle class declaration syntax with a single attribute
                if (syntaxNode is ClassDeclarationSyntax classDeclarationSyntax
                    && classDeclarationSyntax.AttributeLists.Count == 1)
                {
                    // Extract attribute name, we assume there is only 1
                    var firstAttribute = classDeclarationSyntax.AttributeLists.First().Attributes.First();
                    var attributeName = firstAttribute.Name.NormalizeWhitespace().ToFullString();

                    if (attributeName == "InjectAsSingleton" || attributeName == "InjectAsTransient" || attributeName == "InjectAsScoped")
                    {
                        CandidateFields[attributeName].Add(classDeclarationSyntax);
                    }
                }
            }
        }
    }
}

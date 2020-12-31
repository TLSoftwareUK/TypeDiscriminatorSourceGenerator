using System;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using NamespaceDeclarationSyntax = Microsoft.CodeAnalysis.CSharp.Syntax.NamespaceDeclarationSyntax;

namespace TLS.TypeDiscriminatorSourceGenerator
{
    [Generator]
    public class TypeDiscriminatorGenerator : ISourceGenerator
    {
        public void Execute(GeneratorExecutionContext context)
        {
            IEnumerable<SyntaxNode>? allNodes = context.Compilation.SyntaxTrees.SelectMany(s => s.GetRoot().DescendantNodes());
            IEnumerable<AttributeSyntax> allAttributes = allNodes.Where((d) => d.IsKind(SyntaxKind.Attribute)).OfType<AttributeSyntax>();
            ImmutableArray<AttributeSyntax> attributes = allAttributes.Where(d => d.Name.ToString() == "TypeDiscriminator")
                .ToImmutableArray();

            foreach(AttributeSyntax syntax in attributes)
            {
                if (syntax.Parent is null)
                    throw new NullReferenceException();
                
                if (syntax.Parent.Parent is not ClassDeclarationSyntax classDec)
                    throw new NullReferenceException();

                string className = classDec.Identifier.Text;
                
                if (classDec.Parent is not NamespaceDeclarationSyntax namespaceDec)
                    throw new NullReferenceException();
                
                SourceText output = GenerateTypeConverter(context, namespaceDec.Name.ToString(), className);

                context.AddSource($"{className}Converter", output);
            }
            
        }

        private SourceText GenerateTypeConverter(GeneratorExecutionContext context, string nameSpace, string baseClass)
        {
            StringBuilder sBuild = new StringBuilder();
            sBuild.Append(@"
using System;
using System.Text.Json;
using System.Text.Json.Serialization;
{requirednamespaces}

namespace {namespace}
{
public class {classname}Converter : JsonConverter<{classname}>
        {        

        public override bool CanConvert(Type type)
        {
            return typeof({classname}).IsAssignableFrom(type);
        }

        public override {classname} Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.StartObject)
            {
                throw new JsonException();
            }

            if (!reader.Read()
                || reader.TokenType != JsonTokenType.PropertyName
                || reader.GetString() != ""TypeDiscriminator"")
            {
                throw new JsonException();
            }

            if (!reader.Read() || reader.TokenType != JsonTokenType.Number)
            {
                throw new JsonException();
            }

            {classname} baseClass;
            {classname}TypeDiscriminator typeDiscriminator = ({classname}TypeDiscriminator) reader.GetInt32();
            switch (typeDiscriminator)
            {
                {readcases}
                
                  default:
                    throw new NotSupportedException();
            }

            if (!reader.Read() || reader.TokenType != JsonTokenType.EndObject)
            {
                throw new JsonException();
            }

            return baseClass;
        }

        public override void Write(Utf8JsonWriter writer, {classname} value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
 
            switch (value)
            {
                  {writecases}

                  default:
                    throw new NotSupportedException();
            }

            writer.WriteEndObject();
        }

        private enum {classname}TypeDiscriminator
        {
            {values}
        }
    }}");

            (IEnumerable<string> subtypes, IEnumerable<string> names) = FindSubtypes(context, baseClass);

            StringBuilder sReadBuildCases = new StringBuilder();

            StringBuilder sWriterBuildCases = new StringBuilder();
            StringBuilder sNamespaces = new StringBuilder();


            StringBuilder enumValues = new StringBuilder();

            foreach (string name in names)
            {
                sNamespaces.AppendLine($"using {name};");
            }

            foreach (string s in subtypes)
            {
                enumValues.AppendLine($"{s} = {GetDeterministicHashCode(s)},");

                sWriterBuildCases.Append($"case {s} derived{s}:\nwriter.WriteNumber(\"TypeDiscriminator\", (int) {{classname}}TypeDiscriminator.{s});\n" +
                                         $"writer.WritePropertyName(\"TypeValue\");\nJsonSerializer.Serialize(writer, derived{s});\nbreak;\n\n");

                sReadBuildCases.Append($"case {{classname}}TypeDiscriminator.{s}:\nif (!reader.Read() || reader.GetString() != \"TypeValue\")\nthrow new JsonException();\n" +
                                       $"if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)\nthrow new JsonException();" +
                                       $"\nbaseClass = ({s}) JsonSerializer.Deserialize(ref reader, typeof({s}));\nbreak;\n\n");
            }

            sBuild.Replace("{requirednamespaces}", sNamespaces.ToString());
            sBuild.Replace("{readcases}", sReadBuildCases.ToString());
            sBuild.Replace("{writecases}", sWriterBuildCases.ToString());
            sBuild.Replace("{values}", enumValues.ToString());
            sBuild.Replace("{namespace}", nameSpace);
            sBuild.Replace("{classname}", baseClass);

            return SourceText.From(sBuild.ToString(), Encoding.UTF8);
        }

        private (IEnumerable<string>, IEnumerable<string>) FindSubtypes(GeneratorExecutionContext context, string baseClass)
        {
            IEnumerable<SyntaxNode>? allNodes = context.Compilation.SyntaxTrees.SelectMany(s => s.GetRoot().DescendantNodes());
            IEnumerable<ClassDeclarationSyntax> allClasses = allNodes.Where((d) => d.IsKind(SyntaxKind.ClassDeclaration)).OfType<ClassDeclarationSyntax>();
            List<string> classNames = new List<string>();
            List<string> requiredNamespaces = new List<string>();

            foreach (ClassDeclarationSyntax classDec in allClasses)
            {
                if (classDec.Modifiers.ToString().Contains("abstract"))
                    continue;

                if (!classNames.Contains(classDec.Identifier.Text) && IsDerivedFrom(classDec, allClasses, baseClass))
                {
                    
                    classNames.Add(classDec.Identifier.Text);
                    
                    if (classDec.Parent is not NamespaceDeclarationSyntax namespaceDec)
                        throw new NullReferenceException();
                    
                    if (!requiredNamespaces.Contains(namespaceDec.Name.ToString()))
                        requiredNamespaces.Add(namespaceDec.Name.ToString());
                }

            }

            return (classNames, requiredNamespaces);
        }

        private bool IsDerivedFrom(ClassDeclarationSyntax classDec, IEnumerable<ClassDeclarationSyntax> classes, string targetBase)
        {
            if (classDec.BaseList != null)
            {
                foreach (BaseTypeSyntax baseType in classDec.BaseList.Types)
                {
                    string baseTypeName = baseType.Type.ToString();
                    if (targetBase.Equals(baseTypeName))
                        return true;

                    /*if (classes.Any(c => c.Identifier.Text.Equals(targetBase)))
                        return true;*/

                    ClassDeclarationSyntax parentClass = classes.FirstOrDefault(c => c.Identifier.Text.Equals(baseTypeName));

                    if (parentClass != null && IsDerivedFrom(parentClass, classes, targetBase))
                        return true;
                }
            }

            return false;
        }

        private int GetDeterministicHashCode(string str)
        {
            unchecked
            {
                int hash1 = (5381 << 16) + 5381;
                int hash2 = hash1;

                for (int i = 0; i < str.Length; i += 2)
                {
                    hash1 = ((hash1 << 5) + hash1) ^ str[i];
                    if (i == str.Length - 1)
                        break;
                    hash2 = ((hash2 << 5) + hash2) ^ str[i + 1];
                }

                return hash1 + (hash2 * 1566083941);
            }
        }

        public void Initialize(GeneratorInitializationContext context)
        {
            
        }
    }
}

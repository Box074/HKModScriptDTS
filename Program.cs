global using System;
global using System.Reflection;
global using System.Linq;
global using System.IO;
global using System.Collections.Generic;
global using System.Text;

namespace HKModScriptDTS
{
    class Program
    {
        static HashSet<string> classTable = new HashSet<string>();
        static List<char> noChar = new List<char>()
        {
            '`', '$', '<', '>', '\\', '/', ' ', '*','&'
        };
        static Dictionary<string, string> eName = new Dictionary<string, string>()
        {
            ["System.String"] = "string",
            ["System.Int16"] = "number",
            ["System.Int32"] = "number",
            ["System.Int64"] = "number",
            ["System.Single"] = "number",
            ["System.Boolean"] = "boolean",
            ["System.Void"] = "void"
        };
        static string GetDelegateType(Type type)
        {
            var im = type.GetMethod("Invoke");
            if(im == null) return "any";
            StringBuilder sb = new StringBuilder();
            sb.Append("(");
            bool f = true;
            int i = 0;
            foreach (var v in im.GetParameters())
            {
                if (!f) sb.Append(",");
                f = false;
                sb.Append(string.IsNullOrEmpty(v.Name) ? $"arg_{(i++).ToString()}" : v.Name);
                sb.Append(":");
                sb.Append(GetTypeName(v.ParameterType));
            }
            sb.Append(")=>");
            sb.Append(GetTypeName(im.ReturnType));
            return sb.ToString();
        }
        static string GetTypeName(Type type)
        {
            if(type == null || string.IsNullOrEmpty(type.FullName)) return "any";
            if (eName.TryGetValue(type.FullName, out var tn)) return tn;
            if (type.IsSubclassOf(typeof(Delegate)))
            {
                return GetDelegateType(type);
            }
            if(noChar.Any(x => type.FullName.Contains(x))) return "any";
            if (type.IsGenericType || type.IsGenericParameter || type.IsConstructedGenericType
                || type.IsGenericTypeDefinition)
                return "any";

            if (type.IsArray)
            {
                StringBuilder sb = new StringBuilder();
                sb.Append(GetTypeName(type.GetElementType()));
                for (int i = 0; i < type.GetArrayRank(); i++) sb.Append("[]");
                return sb.ToString();
            }
            if (type.IsNested)
            {
                return $"{GetTypeName(type.DeclaringType)}.{type.Name}";
            }
            if(string.IsNullOrEmpty(type.Namespace))
            {
                return $"GLOBAL_{type.FullName}";
            }
            return type.FullName;
        }
        static void ParseType(Type type, StringBuilder sb)
        {
            if (type.IsGenericType) return;
            if(classTable.Contains(type.FullName)) return;
            classTable.Add(type.FullName);
            if (!string.IsNullOrEmpty(type.Namespace) && !type.IsNested)
            {
                sb.Append($"declare namespace {type.Namespace} {{\n");
            }
            else
            {
                sb.Append("export type GLOBAL_");
                sb.Append(type.FullName);
                sb.Append(" = ");
                sb.Append(type.FullName);
                sb.Append(";\n");
            }

            if (type.IsSubclassOf(typeof(Delegate)))
            {
                sb.Append($"export type {type.Name} = {GetDelegateType(type)};\n");
                goto _End;
            }
            
            if (noChar.Any(x => type.FullName.Contains(x))) goto _End;
            sb.Append($"export class ");
            sb.Append(type.Name);
            var ti = type.GetTypeInfo();
            var interfaces = ti.GetInterfaces();
            var hasBase=  type.BaseType != typeof(object) && !type.IsValueType;
            var hasInterfaces = (interfaces?.Length ?? 0) != 0;
            if (hasBase || hasInterfaces)
            {
                sb.Append(" extends ");
                if(hasBase) sb.Append(GetTypeName(type.BaseType));
                if(hasInterfaces)
                {
                    bool f = !hasBase;
                    foreach(var v in interfaces)
                    {
                        if(!f)
                        {
                            sb.Append(",");
                        }
                        f = false;
                        sb.Append(GetTypeName(v));
                    }
                }
            }
            sb.Append(" {\n");
            int i = 0;
            foreach(var v in ti.DeclaredFields)
            {
                if(!v.IsPublic) continue;
                sb.Append("public ");
                sb.Append(string.IsNullOrEmpty(v.Name) ? $"arg_{(i++).ToString()}" : v.Name);
                sb.Append(":");
                sb.Append(GetTypeName(v.FieldType));
                sb.Append(";\n");
            }
            foreach(var v in ti.DeclaredMethods)
            {
                if(!v.IsPublic) continue;
                sb.Append("public ");
                if(v.IsSpecialName && v.IsConstructor)
                {
                    sb.Append("constructor");
                }
                else
                {
                    if(v.IsStatic) sb.Append("static ");
                    sb.Append(v.Name);
                }
                sb.Append("(");
                bool f = true;
                foreach(var p in v.GetParameters())
                {
                    if(!f) sb.Append(",");
                    f = false;
                    sb.Append(string.IsNullOrEmpty(p.Name) ? $"arg_{(i++).ToString()}" : p.Name);
                    sb.Append(":");
                    sb.Append(GetTypeName(p.ParameterType));
                }
                sb.Append(") : ");
                sb.Append(GetTypeName(v.ReturnType));
                sb.Append(";\n");
            }
            foreach(var v in ti.DeclaredConstructors)
            {
                if (!v.IsPublic) continue;
                sb.Append("public ");
                sb.Append("constructor");
                bool f = true;
                sb.Append("(");
                foreach (var p in v.GetParameters())
                {
                    if (!f) sb.Append(",");
                    f = false;
                    sb.Append(string.IsNullOrEmpty(p.Name) ? $"arg_{(i++).ToString()}" : p.Name);
                    sb.Append(":");
                    sb.Append(GetTypeName(p.ParameterType));
                }
                sb.Append(");\n");
            }

            sb.Append("}\n");
        _End:
            if (!string.IsNullOrEmpty(type.Namespace) && !type.IsNested)
            {
                sb.Append("}\n");
            }
        }

        static void Main(string[] args)
        {
            List<Assembly> assemblies = new List<Assembly>();
            
            foreach (var a in args)
            {
                if (Path.GetExtension(a).ToLower() != ".dll") continue;
                try
                {
                    assemblies.Add(Assembly.LoadFrom(a));
                }
                catch (Exception e)
                {
                    Console.Error.WriteLine(e);
                }
            }
            StringBuilder sb = new StringBuilder();
            foreach (var v in assemblies)
            {
                if(v == null) continue;
                try
                {
                    foreach (var t in v.GetTypes())
                    {
                        if (!t.IsNested)
                        {
                            ParseType(t, sb);
                        }
                    }
                }
                catch (Exception e)
                {
                    Console.Error.WriteLine(e);
                }
            }
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(args[0]), "ModScript.d.ts"), sb.ToString());
            Console.ReadLine();
        }
    }
}

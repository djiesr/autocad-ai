using System.Reflection;
using Autodesk.AutoCAD.Runtime;

// Version auto-incrémentée à chaque build (build+revision),
// ce qui permet à AutoCAD de charger plusieurs versions de la DLL via NETLOAD.
[assembly: AssemblyVersion("1.0.*")]
[assembly: AssemblyFileVersion("1.0.0.0")]

// AutoCAD: enregistrer explicitement les commandes et l'extension.
[assembly: CommandClass(typeof(AutocadAI.Commands))]
[assembly: ExtensionApplication(typeof(AutocadAI.Commands))]


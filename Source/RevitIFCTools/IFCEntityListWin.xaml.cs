﻿//
// BIM IFC library: this library works with Autodesk(R) Revit(R) to export IFC files containing model geometry.
// Copyright (C) 2012  Autodesk, Inc.
// 
// This library is free software; you can redistribute it and/or
// modify it under the terms of the GNU Lesser General Public
// License as published by the Free Software Foundation; either
// version 2.1 of the License, or (at your option) any later version.
//
// This library is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU
// Lesser General Public License for more details.
//
// You should have received a copy of the GNU Lesser General Public
// License along with this library; if not, write to the Free Software
// Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301  USA
//

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Forms;
using System.Runtime.Serialization.Json;
using System.IO;
using Revit.IFC.Common.Utility;
using GeometryGym.Ifc;
using Revit.IFC.Export.Utility;
using System.Reflection;
using System.Diagnostics;
namespace RevitIFCTools
{
   /// <summary>
   /// Interaction logic for MainWindow.xaml
   /// </summary>
   public partial class IFCEntityListWin: Window
   {
      SortedSet<string> aggregateEntities;
      string outputFolder = @"c:\temp";
      StreamWriter logF;
      public IFCEntityListWin()
      {
         InitializeComponent();
         textBox_outputFolder.Text = outputFolder; // set default
         button_subtypeTest.IsEnabled = false;
         button_supertypeTest.IsEnabled = false;
         button_Go.IsEnabled = false;
      }

      private void button_browse_Click(object sender, RoutedEventArgs e)
      {
         var dialog = new FolderBrowserDialog();
         dialog.ShowDialog();
         textBox_folderLocation.Text = dialog.SelectedPath;
         if (string.IsNullOrEmpty(textBox_folderLocation.Text))
            return;

         DirectoryInfo dInfo = new DirectoryInfo(dialog.SelectedPath);
         foreach (FileInfo f in dInfo.GetFiles("IFC*.xsd"))
         {
            listBox_schemaList.Items.Add(f.Name);
         }
      }

      /// <summary>
      /// Procees an IFC schema from the IFCXML schema
      /// </summary>
      /// <param name="f">IFCXML schema file</param>
      private void processSchema(FileInfo f)
      {
         ProcessIFCXMLSchema.ProcessIFCSchema(f);

         string schemaName = f.Name.Replace(".xsd", "");

         if (checkBox_outputSchemaTree.IsChecked == true)
         {
            string treeDump = IfcSchemaEntityTree.DumpTree();
            System.IO.File.WriteAllText(outputFolder + @"\entityTree" + schemaName + ".txt", treeDump);
         }

         if (checkBox_outputSchemaEnum.IsChecked == true)
         {
            string dictDump = IfcSchemaEntityTree.DumpEntityDict(schemaName);
            System.IO.File.WriteAllText(outputFolder + @"\entityEnum" + schemaName + ".cs", dictDump);
         }

         // Add aggregate of the entity list into a set
         foreach (KeyValuePair<string,IfcSchemaEntityNode> entry in IfcSchemaEntityTree.EntityDict)
         {
            aggregateEntities.Add(entry.Key);
         }
      }

      private void listBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
      {
         if (listBox_schemaList.SelectedItems.Count > 0)
            button_Go.IsEnabled = true;
         else
            button_Go.IsEnabled = false;
      }

      private void button_Go_Click(object sender, RoutedEventArgs e)
      {
         if (listBox_schemaList.SelectedItems.Count == 0)
            return;

         DirectoryInfo dInfo = new DirectoryInfo(textBox_folderLocation.Text);
         if (dInfo == null)
            return;

         if (aggregateEntities == null)
            aggregateEntities = new SortedSet<string>();
         aggregateEntities.Clear();

         logF = new StreamWriter(System.IO.Path.Combine(outputFolder, "entityList.log"));

         IList<IFCEntityAndPsetList> fxEntityNPsetList = new List<IFCEntityAndPsetList>();

         string jsonFile = outputFolder + @"\IFCEntityAndPsetDefs.json";
         if (File.Exists(jsonFile))
            File.Delete(jsonFile);
         FileStream fs = File.Create(jsonFile);
         DataContractJsonSerializer ser = new DataContractJsonSerializer(typeof(List<IFCEntityAndPsetList>));

         foreach (string fileName in listBox_schemaList.SelectedItems)
         {
            FileInfo f = dInfo.GetFiles(fileName).First();
            processSchema(f);

            ProcessPsetDefinition procPdef = new ProcessPsetDefinition(logF);

            // Add creation of Json file for FORNAX universal template
            string schemaName = f.Name.Replace(".xsd", "");
            IDictionary<string, IfcSchemaEntityNode> entDict = IfcSchemaEntityTree.GetEntityDictFor(schemaName);
            IFCEntityAndPsetList schemaEntities = new IFCEntityAndPsetList();
            schemaEntities.Version = schemaName;
            schemaEntities.EntityList = new HashSet<IFCEntityInfo>();
            schemaEntities.PsetDefList = new HashSet<IFCPropertySetDef>();

            DirectoryInfo[] psdFolders = new DirectoryInfo(System.IO.Path.Combine(textBox_folderLocation.Text, schemaName)).GetDirectories("psd", SearchOption.AllDirectories);
            DirectoryInfo[] underpsdFolders = psdFolders[0].GetDirectories();
            if (underpsdFolders.Count() > 0)
            {
               foreach (DirectoryInfo subDir in psdFolders[0].GetDirectories())
               {
                  procPdef.ProcessSchemaPsetDef(schemaName, subDir);
               }
            }
            else
            {
               procPdef.ProcessSchemaPsetDef(schemaName, psdFolders[0]);
            }

            //Collect information on applicable Psets for Entity
            IDictionary<string, HashSet<string>> entPsetDict = new Dictionary<string, HashSet<string>>();
            schemaEntities.PsetDefList.Add(DefineFXProperties());
            foreach (KeyValuePair<string, IList<VersionSpecificPropertyDef>> pdefEntry in procPdef.allPDefDict)
            {
               foreach(VersionSpecificPropertyDef vPdef in pdefEntry.Value)
               {
                  //if (vPdef.IfcVersion.Equals(schemaName, StringComparison.InvariantCultureIgnoreCase))
                  {
                     IFCPropertySetDef psetDef = new IFCPropertySetDef();
                     psetDef.PsetName = vPdef.PropertySetDef.Name;
                     IList<string> props = new List<string>();
                     foreach (PropertySet.PsetProperty property in vPdef.PropertySetDef.properties)
                     {
                        props.Add(property.Name);
                     }
                     psetDef.Properties = props;
                     schemaEntities.PsetDefList.Add(psetDef);                     

                     // TODO: to check the appl classes either a type or not and check whether the pair (type or without) exists in entDict, if there is add 
                     foreach (string applEntity in vPdef.PropertySetDef.ApplicableClasses)
                     {
                        if (entPsetDict.ContainsKey(applEntity))
                        {
                           entPsetDict[applEntity].Add(vPdef.PropertySetDef.Name);
                        }
                        else
                        {
                           entPsetDict.Add(applEntity, new HashSet<string>(){vPdef.PropertySetDef.Name});
                        }

                        // The Pset will be valid for both the Instance and the Type. Check for that here and add if found
                        string entOrTypePair;
                        if (applEntity.Length > 4 && applEntity.EndsWith("Type"))
                           entOrTypePair = applEntity.Substring(0, applEntity.Length - 4);
                        else
                           entOrTypePair = applEntity + "Type";

                        if (aggregateEntities.Contains(entOrTypePair))
                        {
                           if (entPsetDict.ContainsKey(entOrTypePair))
                           {
                              entPsetDict[entOrTypePair].Add(vPdef.PropertySetDef.Name);
                           }
                           else
                           {
                              entPsetDict.Add(entOrTypePair, new HashSet<string>() { vPdef.PropertySetDef.Name });
                           }
                        }
                     }
                  }
               }
            }


#if FORNAX_EXTENSION
            foreach (IfcPropertySetTemplate pset in LoadUserDefinedPset())
            {
               IFCPropertySetDef psetDef = new IFCPropertySetDef();
               psetDef.PsetName = pset.Name;
               IList<string> props = new List<string>();
               foreach (KeyValuePair<string, GeometryGym.Ifc.IfcPropertyTemplate> property in pset.HasPropertyTemplates)
               {
                  props.Add(property.Key);
               }
               psetDef.Properties = props;
               schemaEntities.PsetDefList.Add(psetDef);
               
               if (entPsetDict.ContainsKey(pset.ApplicableEntity))
               {
                  entPsetDict[pset.ApplicableEntity].Add(pset.Name);
               }
               else
               {
                  entPsetDict.Add(pset.ApplicableEntity, new HashSet<string>() { pset.Name });
               }

               // The Pset will be valid for both the Instance and the Type. Check for that here and add if found
               string entOrTypePair;
               if (pset.ApplicableEntity.Length > 4 && pset.ApplicableEntity.EndsWith("Type"))
                  entOrTypePair = pset.ApplicableEntity.Substring(0, pset.ApplicableEntity.Length - 4);
               else
                  entOrTypePair = pset.ApplicableEntity + "Type";

               if (aggregateEntities.Contains(entOrTypePair))
               {
                  if (entPsetDict.ContainsKey(entOrTypePair))
                  {
                     entPsetDict[entOrTypePair].Add(pset.Name);
                  }
                  else
                  {
                     entPsetDict.Add(entOrTypePair, new HashSet<string>() { pset.Name });
                  }
               }
            }          

#endif

            // For every entity of the schema, collect the list of PredefinedType (obtained from the xsd), and collect all applicable
            //  Pset Definitions collected above
            foreach (KeyValuePair<string, IfcSchemaEntityNode> ent in entDict)
            {
               IFCEntityInfo entInfo = new IFCEntityInfo();

               // The abstract entity type is not going to be listed here as they can never be created
               if (ent.Value.isAbstract)
                  continue;

               // Collect only the IfcProducts or IfcGroup
               //if (!ent.Value.IsSubTypeOf("IfcProduct") && !ent.Value.IsSubTypeOf("IfcGroup") && !ent.Value.IsSubTypeOf("IfcTypeProduct"))
               //   continue;
               if (!IfcSchemaEntityTree.IsSubTypeOf(ent.Value.Name, "IfcProduct")
                  && !IfcSchemaEntityTree.IsSubTypeOf(ent.Value.Name, "IfcTypeProduct")
                  && !IfcSchemaEntityTree.IsSubTypeOf(ent.Value.Name, "IfcGroup", strict: false))
                  continue;

               entInfo.Entity = ent.Key;
               if (!string.IsNullOrEmpty(ent.Value.PredefinedType))
               {
                  if (IfcSchemaEntityTree.PredefinedTypeEnumDict.ContainsKey(ent.Value.PredefinedType))
                  {
                     entInfo.PredefinedType = IfcSchemaEntityTree.PredefinedTypeEnumDict[ent.Value.PredefinedType];
                  }
               }
               
               // Get Pset list that is applicable to this entity type
               if (entPsetDict.ContainsKey(entInfo.Entity))
               {
                  entInfo.PropertySets = entPsetDict[entInfo.Entity].ToList();
               }
#if FORNAX_EXTENSION
               // Add FORNAX special property sets IFCATTRIBUTES
               if (entInfo.PropertySets == null)
                  entInfo.PropertySets = new List<string>() { "IFCATTRIBUTES" };
               else
                  entInfo.PropertySets.Add("IFCATTRIBUTES");
               // TODO: Add the pset definition of IFCATTRIBUTES to ... (probably has to be dne earlier)
#endif

               // Collect Pset that is applicable to the supertype of this entity
               IList<IfcSchemaEntityNode> supertypeList = IfcSchemaEntityTree.FindAllSuperTypes(entInfo.Entity, 
                  "IfcProduct", "IfcTypeProduct", "IfcObject");
               if (supertypeList != null && supertypeList.Count > 0)
               {
                  foreach(IfcSchemaEntityNode superType in supertypeList)
                  {
                     if (entPsetDict.ContainsKey(superType.Name))
                     {
                        if (entInfo.PropertySets == null)
#if FORNAX_EXTENSION
                           entInfo.PropertySets = new List<string>() { "IFCATTRIBUTES" };
#else
                           entInfo.PropertySets = new List<string>();
#endif
                        foreach (string pset in entPsetDict[superType.Name])
                           entInfo.PropertySets.Add(pset);
                     }
                  }
               }

               schemaEntities.EntityList.Add(entInfo);
            }
            fxEntityNPsetList.Add(schemaEntities);
         }
         ser.WriteObject(fs, fxEntityNPsetList);
         fs.Close();

         if (aggregateEntities.Count > 0)
         {
            string entityList;
            entityList = "using System;"
                        + "\nusing System.Collections.Generic;"
                        + "\nusing System.Linq;"
                        + "\nusing System.Text;"
                        + "\n"
                        + "\nnamespace Revit.IFC.Common.Enums"
                        + "\n{"
                        + "\n\t/// <summary>"
                        + "\n\t/// IFC entity types. Combining IFC2x3 and IFC4 (Add2) entities."
                        + "\n\t/// List of Entities for IFC2x is found in IFC2xEntityType.cs"
                        + "\n\t/// List of Entities for IFC4 is found in IFC4EntityType.cs"
                        + "\n\t/// </summary>"
                        + "\n\tpublic enum IFCEntityType"
                        + "\n\t{";

            foreach (string ent in aggregateEntities)
            {
               entityList += "\n\t\t/// <summary>"
                           + "\n\t\t/// IFC Entity " + ent + " enumeration"
                           + "\n\t\t/// </summary>"
                           + "\n\t\t" + ent + ",\n";
            }
            entityList += "\n\t\tUnknown,"
                        + "\n\t\tDontExport"
                        + "\n\t}"
                        + "\n}";
            System.IO.File.WriteAllText(outputFolder + @"\IFCEntityType.cs", entityList);
         }

         foreach (IFCEntityAndPsetList fxEntityNPset in fxEntityNPsetList)
         {
            string entityList;
            entityList = "using System;"
                        + "\nusing System.Collections.Generic;"
                        + "\nusing System.Linq;"
                        + "\nusing System.Text;"
                        + "\n"
                        + "\nnamespace Revit.IFC.Common.Enums." + fxEntityNPset.Version
                        + "\n{"
                        + "\n\t/// <summary>"
                        + "\n\t/// List of Entities for " + fxEntityNPset.Version
                        + "\n\t/// </summary>"
                        + "\n\tpublic enum EntityType"
                        + "\n\t{";

            foreach (IFCEntityInfo entInfo in fxEntityNPset.EntityList)
            {
               entityList += "\n\t\t/// <summary>"
                           + "\n\t\t/// IFC Entity " + entInfo.Entity + " enumeration"
                           + "\n\t\t/// </summary>"
                           + "\n\t\t" + entInfo.Entity + ",\n";
            }
            entityList += "\n\t\tUnknown,"
                        + "\n\t\tDontExport"
                        + "\n\t}"
                        + "\n}";
            System.IO.File.WriteAllText(outputFolder + @"\" + fxEntityNPset.Version + "EntityType.cs", entityList);
         }

         // Only allows test when only one schema is selected
         if (listBox_schemaList.SelectedItems.Count == 1)
         {
            button_subtypeTest.IsEnabled = true;
            button_supertypeTest.IsEnabled = true;
         }
         else
         {
            button_subtypeTest.IsEnabled = false;
            button_supertypeTest.IsEnabled = false;
         }

         if (logF != null)
            logF.Close();
      }

      private void button_Cancel_Click(object sender, RoutedEventArgs e)
      {
         if (logF != null)
            logF.Close();
         Close();
      }

      private void button_browseOutputFolder_Click(object sender, RoutedEventArgs e)
      {
         var dialog = new FolderBrowserDialog();
         dialog.ShowDialog();
         textBox_outputFolder.Text = dialog.SelectedPath;
         outputFolder = dialog.SelectedPath;
      }

      private void button_subtypeTest_Click(object sender, RoutedEventArgs e)
      {
         if (string.IsNullOrEmpty(textBox_type1.Text) || string.IsNullOrEmpty(textBox_type2.Text))
            return;

         bool res = IfcSchemaEntityTree.IsSubTypeOf(textBox_type1.Text, textBox_type2.Text);
         if (res)
            checkBox_testResult.IsChecked = true;
         else
            checkBox_testResult.IsChecked = false;
      }

      private void button_supertypeTest_Click(object sender, RoutedEventArgs e)
      {
         if (string.IsNullOrEmpty(textBox_type1.Text) || string.IsNullOrEmpty(textBox_type2.Text))
            return;

         bool res = IfcSchemaEntityTree.IsSuperTypeOf(textBox_type1.Text, textBox_type2.Text);
         if (res)
            checkBox_testResult.IsChecked = true;
         else
            checkBox_testResult.IsChecked = false;
      }

      private void textBox_type1_TextChanged(object sender, TextChangedEventArgs e)
      {
         checkBox_testResult.IsChecked = false;
      }

      private void textBox_type2_TextChanged(object sender, TextChangedEventArgs e)
      {
         checkBox_testResult.IsChecked = false;
      }

      private void textBox_outputFolder_TextChanged(object sender, TextChangedEventArgs e)
      {
         outputFolder = textBox_outputFolder.Text;
      }

#if FORNAX_EXTENSION
      IFCPropertySetDef DefineFXProperties ()
      {  
         // For IFCATTRIBUTES
         IFCPropertySetDef pset = new IFCPropertySetDef();
         pset.PsetName = "IFCATTRIBUTES";
         IList<string> props = new List<string>();
         props.Add("AreaClassification");
         props.Add("Building Name");
         props.Add("Description");
         props.Add("DrainageBoundary");
         props.Add("Checkbox");
         props.Add("Level");
         props.Add("LongName");
         props.Add("Material");
         props.Add("Name");
         props.Add("ObjectType");
         props.Add("OccupancyType");
         props.Add("PredefinedType");
         props.Add("ProjectDevelopmentType");
         props.Add("Project Location");
         props.Add("System");
         pset.Properties = props;                       

         return pset;
      }
      public static IEnumerable<IfcPropertySetTemplate> LoadUserDefinedPset()
      {
         List<IfcPropertySetTemplate> userDefinedPsets = new List<IfcPropertySetTemplate>();

         try
         {
            string filename = "SGPset.txt";
            string extension = System.IO.Path.GetExtension(filename);
            var path = @"..\..\SGPset.txt";
            if (string.Compare(extension, ".ifcxml", true) == 0 || string.Compare(extension, ".ifcjson", true) == 0 || string.Compare(extension, ".ifc", true) == 0)
            {
               DatabaseIfc db = new DatabaseIfc(filename);
               IfcContext context = db.Context;
               if (context == null)
                  return userDefinedPsets;
               foreach (IfcRelDeclares relDeclares in context.Declares)
               {
                  userDefinedPsets.AddRange(relDeclares.RelatedDefinitions.OfType<IfcPropertySetTemplate>());
               }
            }
            else
            {
               using (StreamReader sr = new StreamReader(path))
               {
                  string line;

                  DatabaseIfc db = new DatabaseIfc(false, ReleaseVersion.IFC4);
                  IfcPropertySetTemplate userDefinedPset = null;
                  while ((line = sr.ReadLine()) != null)
                  {
                     line.TrimStart(' ', '\t');

                     if (String.IsNullOrEmpty(line)) continue;
                     if (line[0] != '#')
                     {
                        // Format: PropertSet: <Pset_name> I[nstance]/T[ype] <IFC entity list separated by ','> 
                        //              Property_name   Data_type   Revit_Parameter
                        // ** For now it only works for simple property with single value (datatype supported: Text, Integer, Real and Boolean)

                        string[] split = line.Split(new char[] { '\t' }, StringSplitOptions.RemoveEmptyEntries);
                        if (string.Compare(split[0], "PropertySet:", true) == 0)
                        {
                           userDefinedPset = new IfcPropertySetTemplate(db, split.Length > 2 ? split[1] : "Unknown");
                           if (split.Count() >= 4)         // Any entry with less than 3 par is malformed
                           {
                              switch (split[2][0])
                              {
                                 case 'T':
                                    userDefinedPset.TemplateType = IfcPropertySetTemplateTypeEnum.PSET_TYPEDRIVENONLY;
                                    break;
                                 case 'I':
                                    userDefinedPset.TemplateType = IfcPropertySetTemplateTypeEnum.PSET_OCCURRENCEDRIVEN;
                                    break;
                                 default:
                                    userDefinedPset.TemplateType = IfcPropertySetTemplateTypeEnum.PSET_OCCURRENCEDRIVEN;
                                    break;
                              }
                              userDefinedPset.ApplicableEntity = string.Join(",", split[3].Split(new char[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries));
                              userDefinedPsets.Add(userDefinedPset);
                           }
                        }
                        else
                        {
                           if (split.Count() >= 2)
                           {
                              string propertyTemplateName = split[0];
                              IfcSimplePropertyTemplate propertyDefUnit = userDefinedPset[propertyTemplateName] as IfcSimplePropertyTemplate;
                              if (propertyDefUnit == null)
                                 userDefinedPset.AddPropertyTemplate(propertyDefUnit = new IfcSimplePropertyTemplate(db, split[0]));
                              if (split.Count() >= 3 && !string.IsNullOrEmpty(split[2]))
                              {
                                 new IfcRelAssociatesClassification(new IfcClassificationReference(db) { Identification = split[2] }, propertyDefUnit);
                              }
                              if (!string.IsNullOrEmpty(split[1]))
                                 propertyDefUnit.PrimaryMeasureType = "Ifc" + split[1];
                           }
                        }
                     }
                  }
               }
            }
         }
         catch (Exception e)
         {
            Console.WriteLine("The file could not be read:");
            Console.WriteLine(e.Message);
         }
         return userDefinedPsets;
      }
      private static string GetUserDefPsetFilename()
      {
         string directory = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
         return directory + @"\" + ExporterCacheManager.ExportOptionsCache.SelectedConfigName + @".txt";
      }
#endif
   }
}

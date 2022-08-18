using System;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using System.Reflection;
using VMS.TPS.Common.Model.API;
using VMS.TPS.Common.Model.Types;

// TODO: Replace the following version attributes by creating AssemblyInfo.cs. You can do this in the properties of the Visual Studio project.
[assembly: AssemblyVersion("1.0.0.1")]
[assembly: AssemblyFileVersion("1.0.0.1")]
[assembly: AssemblyInformationalVersion("1.0")]

// TODO: Uncomment the following line if the script requires write access.
[assembly: ESAPIScript(IsWriteable = true)]

namespace AutoPlan_Prostate
{
    class Program
    {
        private static string _patientId;

        [STAThread]
        static void Main(string[] args)
        {
            try
            {
                if (args.Any())
                {
                    _patientId = args.First();
                }
                using (Application app = Application.CreateApplication())
                {
                    Execute(app);
                }
            }
            catch (Exception e)
            {
                Console.Error.WriteLine(e.ToString());
            }
            Console.ReadLine();
        }
        static void Execute(Application app)
        {
            if(string.IsNullOrEmpty(_patientId))
            {
                Console.WriteLine("Enter Patient Id:");
                _patientId = Console.ReadLine();
            }

            Patient patient = app.OpenPatientById(_patientId);
            if (patient == null)
            {
                Console.WriteLine($"Could not find {_patientId}"); 
                return;
            }
            Console.WriteLine($"Patient: {patient.Name} opened.");
            
            //must have this line for write-enabled scripting.
            patient.BeginModifications();
            //create a course.
            Course course = null;
            if (patient.Courses.Any(c => c.Id.Equals("AutoCourse")))
            {
                course = patient.Courses.FirstOrDefault(c => c.Id.Equals("AutoCourse"));
            }
            else { course = patient.AddCourse(); course.Id = "AutoCourse"; }
            //Structure set Ids are not unique, so a check for the Image Id is needed as well.
            ExternalPlanSetup plan = course.AddExternalPlanSetup(patient.StructureSets.FirstOrDefault(ss=> ss.Id.Equals("CT_1") && ss.Image.Id.Equals("CT_2")));
            Console.WriteLine($"Plan: {plan.Id} in course {course.Id}.");
            double[] gantryAngles = new double[] { 220, 255, 290, 325, 25, 55, 95, 130 };
            ExternalBeamMachineParameters parameters = new ExternalBeamMachineParameters("HESN5", "10X", 600, "STATIC",null);
            foreach (double ga in gantryAngles)
            {
                plan.AddStaticBeam(parameters,
                    new VRect<double>(-50,-50,50,50),
                    0,
                    ga,
                    0,
                    plan.StructureSet.Image.UserOrigin);
            }
            Console.WriteLine($"Generated {plan.Beams.Count()} fields");
            plan.SetPrescription(28, new DoseValue(250,DoseValue.DoseUnit.cGy), 1.0); //set rx.
            //find the target volume.
            Structure target = plan.StructureSet.Structures.FirstOrDefault(s=> s.Id.Equals("PTVprost SV marg"));
            //If there is no ring. Generate a ring.
            Structure ring = null;
            if(plan.StructureSet.Structures.Any(s=>s.Id == "NS_Ring05"))
            {
                ring = plan.StructureSet.Structures.FirstOrDefault(s => s.Id.Equals("NS_Ring05"));
            }
            else
            {
                ring = plan.StructureSet.AddStructure("CONTROL", "NS_Ring05");
            }
            ring.SegmentVolume = target.SegmentVolume.Margin(5).Sub(target.SegmentVolume);
            //NEW to V16.1
            StringBuilder errString = new StringBuilder();
            plan.SetTargetStructureIfNoDose(target, errString);
            Console.WriteLine("Please Select a Rapidplan Model.");
            int rp_i = 0;
            foreach(var rp in app.Calculation.GetDvhEstimationModelSummaries())//new ro 16.1 --> Claculation class.
            {
                Console.WriteLine($"[{rp_i}]. {rp.Name} - {rp.TreatmentSite}");
                rp_i++;
            }
            int rpSelect = Convert.ToInt32(Console.ReadLine());
            var rpModel = app.Calculation.GetDvhEstimationModelSummaries().ElementAt(rpSelect);
            Dictionary<string, string> structureMatches = new Dictionary<string, string>();
            Dictionary<string, DoseValue> targetMatches = new Dictionary<string, DoseValue>();
            foreach(var structure in app.Calculation.GetDvhEstimationModelStructures(rpModel.ModelUID))
            {
                if (structure.StructureType == DVHEstimationStructureType.PTV)
                {
                    structureMatches.Add(target.Id, structure.Id);
                    targetMatches.Add(target.Id, plan.TotalDose);
                }
                else
                {
                    if (plan.StructureSet.Structures.Any(s=> s.Id.Equals(structure.Id, StringComparison.OrdinalIgnoreCase)))
                    {
                        structureMatches.Add(plan.StructureSet.Structures.First(s => s.Id.Equals(structure.Id, StringComparison.OrdinalIgnoreCase)).Id, structure.Id);
                    }
                }
            }
            Console.WriteLine("Calculated DVH Estimates...");
            plan.CalculateDVHEstimates(rpModel.Name, targetMatches, structureMatches);
            plan.OptimizationSetup.AddPointObjective(ring, OptimizationObjectiveOperator.Upper, plan.TotalDose * 1.05, 0.0, 100.0);
            Console.WriteLine("Optimizing...");
            plan.Optimize();
            Console.WriteLine("Calculating Leaf Motions...");
            plan.CalculateLeafMotions();
            Console.WriteLine("Calculating Dose...");
            plan.CalculateDose();
            Console.WriteLine("Saving...");
            app.SaveModifications();

        }
    }
}

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
            ExternalBeamMachineParameters parameters = new ExternalBeamMachineParameters("HESNS", "10X", 600, "STATIC",null);
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
            Console.WriteLine("Calculating Dose...");
            plan.CalculateDose();
            Console.WriteLine("Saving...");
            app.SaveModifications();

        }
    }
}

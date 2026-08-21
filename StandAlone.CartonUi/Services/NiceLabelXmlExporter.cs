using StandAlone.CartonUi.Models;
using System.Xml.Linq;

namespace StandAlone.CartonUi.Services;

/// <summary>
/// Exports label data to NiceLabel XML format.
/// Writes to a custom file path specified in settings.
/// </summary>
public class NiceLabelXmlExporter
{
    private readonly string _outputPath;

    public NiceLabelXmlExporter(string outputPath)
    {
        _outputPath = outputPath;
    }

    /// <summary>
    /// Exports a box record to NiceLabel XML file.
    /// </summary>
    public async Task<bool> ExportAsync(BoxRecord box, StackerRecord? stacker, string itemDisplay, CancellationToken ct)
    {
        try
        {
            // Validate output path
            if (string.IsNullOrWhiteSpace(_outputPath))
                return false;

            // Ensure directory exists
            var outputDir = Path.GetDirectoryName(_outputPath);
            if (!string.IsNullOrEmpty(outputDir))
                Directory.CreateDirectory(outputDir);

            // Generate NiceLabel XML
            var xml = GenerateNiceLabelXml(box, stacker, itemDisplay);

            // Write to file
            await File.WriteAllTextAsync(_outputPath, xml, ct);
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"NiceLabel XML export error: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Exports a manual carton print job to NiceLabel XML format.
    /// </summary>
    public async Task<bool> ExportManualCartonAsync(ManualCartonPrintJob job, CancellationToken ct)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_outputPath))
                return false;

            var outputDir = Path.GetDirectoryName(_outputPath);
            if (!string.IsNullOrEmpty(outputDir))
                Directory.CreateDirectory(outputDir);

            var xml = GenerateManualCartonXml(job);
            await File.WriteAllTextAsync(_outputPath, xml, ct);
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"NiceLabel manual carton export error: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Generates NiceLabel XML document.
    /// </summary>
    private static string GenerateNiceLabelXml(BoxRecord box, StackerRecord? stacker, string itemDisplay)
    {
        var root = new XElement("Document",
            new XAttribute("Type", "Label"),
            new XAttribute("CreatedAt", DateTime.Now.ToString("O")),
            new XElement("LabelData",
                new XElement("BoxRecord",
                    new XElement("RecId", box.RecId),
                    new XElement("LineId", box.LineId),
                    new XElement("MakeTime", box.MakeTime.ToString("O")),
                    new XElement("StackNum", box.StackNum.Trim()),
                    new XElement("PlcMsg", box.PlcMsg),
                    new XElement("ErrMsg", box.ErrMsg),
                    new XElement("PrintNum", box.PrintNum),
                    new XElement("ItemDisplay", itemDisplay),
                    new XElement("SorterMessage", box.SorterMessage)
                ),
                stacker != null ? new XElement("StackerRecord",
                    new XElement("LineId", stacker.LineId),
                    new XElement("StackNum", stacker.StackNum.Trim()),
                    new XElement("ItemNumber", stacker.ItemNumber),
                    new XElement("Qty", stacker.Qty),
                    new XElement("Shade", stacker.Shade),
                    new XElement("Size", stacker.Size)
                ) : null
            )
        );

        var doc = new XDocument(
            new XDeclaration("1.0", "utf-8", "yes"),
            root
        );

        return doc.ToString();
    }

    private static string GenerateManualCartonXml(ManualCartonPrintJob job)
    {
        var root = new XElement("Document",
            new XAttribute("Type", "ManualCarton"),
            new XAttribute("CreatedAt", DateTime.Now.ToString("O")),
            new XElement("LabelData",
                new XElement("ManualCartonJob",
                    new XElement("ItemNumber", job.ItemNumber),
                    new XElement("Quantity", job.Quantity),
                    new XElement("Inspector", job.Inspector),
                    new XElement("Shift", job.Shift),
                    new XElement("LabelSize", job.LabelSize),
                    new XElement("PartDescription", job.PartDescription),
                    new XElement("RequestedBy", job.RequestedBy),
                    new XElement("RequestedAt", job.RequestedAt.ToString("O"))
                )
            )
        );

        var doc = new XDocument(
            new XDeclaration("1.0", "utf-8", "yes"),
            root
        );

        return doc.ToString();
    }
}

public sealed record ManualCartonPrintJob(
    string ItemNumber,
    int Quantity,
    string Inspector,
    int Shift,
    string LabelSize,
    string PartDescription,
    string RequestedBy,
    DateTime RequestedAt,
    string ShopOrder = "",
    string ShadeOverride = "",
    string Caliber = "",
    string LabelFormat = "");

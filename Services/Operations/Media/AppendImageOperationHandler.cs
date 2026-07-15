using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using DocumentModel = TxTextControl.McpServer.Models.DocumentModel;
using TxTextControl.McpServer.Models.Requests;
using TxTextControl.McpServer.Models.Responses;
using TXTextControl;
using TxImage = TXTextControl.Image;

namespace TxTextControl.McpServer.Services.Operations;

public sealed class AppendImageOperationHandler : IDocumentOperationHandler
{
    private static readonly Dictionary<string, int> ImportFilterIndexes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".bmp"] = 1,
        [".tif"] = 2,
        [".tiff"] = 2,
        [".wmf"] = 3,
        [".png"] = 4,
        [".jpg"] = 5,
        [".jpeg"] = 5,
        [".gif"] = 6,
        [".emf"] = 7,
        [".svg"] = 8
    };

    public string Type => MediaCapabilityPack.AppendImage;
    public string CapabilityPack => MediaCapabilityPack.PackName;
    public DocumentOperationDescriptor Descriptor { get; } = new()
    {
        Type = MediaCapabilityPack.AppendImage,
        CapabilityPack = MediaCapabilityPack.PackName,
        Description = "Appends an image from a TX Text Control supported file format, base64 payload, or data URI.",
        Intent = "Use for document images that should be embedded in exported documents with optional alignment, location, text flow, and scaling.",
        RequiredProperties = ["type", "imagePath or imageBase64"],
        OptionalProperties =
        [
            "altText",
            "imageFormat",
            "filterIndex",
            "imageName",
            "target",
            "width",
            "height",
            "unit",
            "horizontalScaling",
            "verticalScaling",
            "insertionMode",
            "alignment",
            "textPosition",
            "pageNumber",
            "locationX",
            "locationY",
            "locationUnit",
            "sizeable",
            "moveable",
            "saveMode"
        ],
        Properties = new()
        {
            ["imagePath"] = "Absolute or server-accessible image path. Supported: bmp, tif, wmf, png, jpg, jpeg, gif, emf, svg.",
            ["imageBase64"] = "Base64 image bytes or a data URI. Supported formats match TX Text Control image import filters.",
            ["imageFormat"] = "Optional image format for base64 sources: bmp, tif, wmf, png, jpg, jpeg, gif, emf, or svg.",
            ["filterIndex"] = "Optional explicit TX image import filter index: 1 bmp, 2 tif, 3 wmf, 4 png, 5 jpg/jpeg, 6 gif, 7 emf, 8 svg.",
            ["imageName"] = "Optional TX image frame name.",
            ["target"] = "Optional insertion target: body, header, footer, firstPageHeader, firstPageFooter, evenHeader, or evenFooter. Defaults to body.",
            ["altText"] = "Optional accessibility description.",
            ["width"] = "Optional neutral-model requested width metadata. TX runtime resizing uses horizontalScaling and verticalScaling.",
            ["height"] = "Optional neutral-model requested height metadata. TX runtime resizing uses horizontalScaling and verticalScaling.",
            ["unit"] = "Optional size unit for neutral width and height metadata: px, pt, in, cm, mm, or twips.",
            ["horizontalScaling"] = "Optional TX image horizontal scaling factor in percent.",
            ["verticalScaling"] = "Optional TX image vertical scaling factor in percent.",
            ["insertionMode"] = "Optional text flow for aligned or located images: aboveText, belowText, displaceText, or displaceCompleteLines. Omit for character-like insertion.",
            ["alignment"] = "Optional paragraph-relative alignment: left, right, center, or centered.",
            ["textPosition"] = "Optional text position. -1 inserts at the current input position; omitted appends at the document end.",
            ["pageNumber"] = "Optional 1-based page number for page-relative located image insertion. Requires locationX and locationY.",
            ["locationX"] = "Optional X location for floating image insertion.",
            ["locationY"] = "Optional Y location for floating image insertion.",
            ["locationUnit"] = "Optional unit for locationX/locationY: px, pt, in, cm, mm, or twips. Defaults to unit, then twips.",
            ["sizeable"] = "Optional TX frame Sizeable flag.",
            ["moveable"] = "Optional TX frame Moveable flag.",
            ["saveMode"] = "Optional TX image save mode: data or reference."
        },
        Example = new()
        {
            ["type"] = MediaCapabilityPack.AppendImage,
            ["imagePath"] = "C:\\Images\\chart.png",
            ["altText"] = "Quarterly sales chart",
            ["horizontalScaling"] = 75,
            ["verticalScaling"] = 75,
            ["alignment"] = "centered",
            ["insertionMode"] = "displaceText"
        },
        ModelEffects = ["Adds a document.sections[].blocks[] image block."],
        RequiresTxExecution = true
    };

    public OperationResult Apply(DocumentOperationContext context, DocumentOperation operation, int index)
    {
        var hasImagePath = !string.IsNullOrWhiteSpace(operation.ImagePath);
        var hasImageBase64 = !string.IsNullOrWhiteSpace(operation.ImageBase64);
        if (hasImagePath == hasImageBase64)
        {
            throw new ArgumentException("append_image requires exactly one of imagePath or imageBase64.");
        }

        using var imageSource = CreateImageSource(operation);

        var tx = context.TextControl;
        var textPosition = operation.TextPosition ?? -1;
        if (!operation.TextPosition.HasValue)
        {
            var insertionPosition = (tx.Text ?? string.Empty).Length;
            tx.Selection = new Selection(insertionPosition, 0);
        }

        var txImage = imageSource.CreateImage();
        ApplyImageProperties(txImage, operation, imageSource);
        var target = ResolveImageTarget(operation.Target);

        if (!AddImage(tx, target, txImage, operation, textPosition))
        {
            throw new InvalidOperationException("The image could not be inserted into the document.");
        }

        var imageId = Guid.NewGuid().ToString("N");
        var blockIndex = AddImageToModel(context, target, imageId, imageSource, operation);
        var modelLocation = GetModelLocation(context, target, blockIndex);

        return new OperationResult
        {
            Index = index,
            Type = Type,
            Detail = $"Appended image '{imageSource.DisplaySource}' to {target.ModelName}.",
            TargetType = "image",
            TargetId = imageId,
            Location = modelLocation,
            Metadata = new Dictionary<string, object?>
            {
                ["sectionIndex"] = context.CurrentSectionIndex,
                ["blockIndex"] = blockIndex,
                ["target"] = target.ModelName,
                ["imageId"] = imageId,
                ["source"] = imageSource.ModelSource,
                ["altText"] = operation.AltText,
                ["width"] = operation.Width,
                ["height"] = operation.Height,
                ["unit"] = string.IsNullOrWhiteSpace(operation.Unit) ? "px" : operation.Unit.Trim(),
                ["horizontalScaling"] = operation.HorizontalScaling,
                ["verticalScaling"] = operation.VerticalScaling,
                ["insertionMode"] = operation.InsertionMode,
                ["alignment"] = operation.Alignment,
                ["textPosition"] = textPosition,
                ["pageNumber"] = operation.PageNumber,
                ["locationX"] = operation.LocationX,
                ["locationY"] = operation.LocationY,
                ["locationUnit"] = operation.LocationUnit,
                ["sizeable"] = operation.Sizeable,
                ["moveable"] = operation.Moveable,
                ["saveMode"] = operation.SaveMode
            }
        };
    }

    private static ImageSource CreateImageSource(DocumentOperation operation)
    {
        if (!string.IsNullOrWhiteSpace(operation.ImagePath))
        {
            var imagePath = operation.ImagePath.Trim();
            if (!File.Exists(imagePath))
            {
                throw new FileNotFoundException("The image file was not found.", imagePath);
            }

            var filterIndex = ResolveFilterIndex(operation, Path.GetExtension(imagePath));
            return ImageSource.FromPath(imagePath, filterIndex);
        }

        var (base64, format) = NormalizeBase64Source(operation.ImageBase64!, operation.ImageFormat);
        ResolveFilterIndex(operation, format);

        var bytes = Convert.FromBase64String(base64);
        return ImageSource.FromBase64(bytes, format);
    }

    private static (string Base64, string? Format) NormalizeBase64Source(string imageBase64, string? imageFormat)
    {
        var trimmed = imageBase64.Trim();
        var dataUriMatch = Regex.Match(
            trimmed,
            @"^data:image/(?<format>[^;]+);base64,(?<data>.+)$",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

        if (!dataUriMatch.Success)
        {
            return (trimmed, imageFormat);
        }

        var format = string.IsNullOrWhiteSpace(imageFormat)
            ? dataUriMatch.Groups["format"].Value
            : imageFormat;

        return (dataUriMatch.Groups["data"].Value, format);
    }

    private static int ResolveFilterIndex(DocumentOperation operation, string? formatOrExtension)
    {
        if (operation.FilterIndex.HasValue)
        {
            if (operation.FilterIndex.Value < 1 || operation.FilterIndex.Value > 8)
            {
                throw new NotSupportedException("filterIndex must be between 1 and 8 for TX Text Control image import.");
            }

            return operation.FilterIndex.Value;
        }

        if (string.IsNullOrWhiteSpace(formatOrExtension))
        {
            throw new ArgumentException("imageFormat or filterIndex is required when imageBase64 is not a data URI.");
        }

        var normalized = formatOrExtension.Trim();
        if (!normalized.StartsWith(".", StringComparison.Ordinal))
        {
            normalized = "." + normalized;
        }

        if (!ImportFilterIndexes.TryGetValue(normalized, out var filterIndex))
        {
            throw new NotSupportedException(
                $"Unsupported image format '{formatOrExtension}'. TX Text Control import supports: .bmp, .tif, .tiff, .wmf, .png, .jpg, .jpeg, .gif, .emf, .svg.");
        }

        return filterIndex;
    }

    private static void ApplyImageProperties(TxImage txImage, DocumentOperation operation, ImageSource imageSource)
    {
        txImage.Name = string.IsNullOrWhiteSpace(operation.ImageName)
            ? imageSource.DefaultName
            : operation.ImageName.Trim();
        txImage.DescriptiveText = operation.AltText ?? string.Empty;
        txImage.SaveMode = ResolveSaveMode(operation.SaveMode);
        txImage.Sizeable = operation.Sizeable ?? false;

        if (operation.Moveable.HasValue)
        {
            txImage.Moveable = operation.Moveable.Value;
        }

        if (operation.HorizontalScaling.HasValue)
        {
            txImage.HorizontalScaling = ValidateScaling(operation.HorizontalScaling.Value, nameof(operation.HorizontalScaling));
        }

        if (operation.VerticalScaling.HasValue)
        {
            txImage.VerticalScaling = ValidateScaling(operation.VerticalScaling.Value, nameof(operation.VerticalScaling));
        }
    }

    private static int ValidateScaling(int value, string propertyName)
    {
        if (value <= 0)
        {
            throw new ArgumentException($"{propertyName} must be greater than 0.");
        }

        return value;
    }

    private static ImageSaveMode ResolveSaveMode(string? saveMode)
        => string.IsNullOrWhiteSpace(saveMode)
            ? ImageSaveMode.SaveAsData
            : saveMode.Trim().ToLowerInvariant() switch
            {
                "data" or "saveasdata" => ImageSaveMode.SaveAsData,
                "reference" or "file" or "saveasfilereference" => ImageSaveMode.SaveAsFileReference,
                _ => throw new ArgumentException("saveMode must be either data or reference.")
            };

    private static bool AddImage(ServerTextControl tx, ImageTarget target, TxImage txImage, DocumentOperation operation, int textPosition)
    {
        if (target.HeaderFooterType.HasValue)
        {
            var headerFooter = GetOrCreateHeaderFooter(tx, target.HeaderFooterType.Value);
            if (!operation.TextPosition.HasValue)
            {
                headerFooter.Selection.Start = GetHeaderFooterTextLength(headerFooter);
                headerFooter.Selection.Length = 0;
            }

            return AddImage(headerFooter.Images, txImage, operation, textPosition);
        }

        return AddImage(tx.Images, txImage, operation, textPosition);
    }

    private static TXTextControl.HeaderFooter GetOrCreateHeaderFooter(ServerTextControl tx, HeaderFooterType type)
    {
        var collection = tx.HeadersAndFooters;
        var headerFooter = collection.GetItem(type);
        if (headerFooter is not null)
        {
            return headerFooter;
        }

        if (!collection.Add(type))
        {
            throw new InvalidOperationException($"TX Text Control could not add {type}.");
        }

        return collection.GetItem(type)
            ?? throw new InvalidOperationException($"TX Text Control added {type}, but it could not be found.");
    }

    private static int GetHeaderFooterTextLength(TXTextControl.HeaderFooter headerFooter)
        => headerFooter.Paragraphs
            .Cast<TXTextControl.Paragraph>()
            .Sum(paragraph => paragraph.Text?.Length ?? 0);

    private static bool AddImage(ImageCollection images, TxImage txImage, DocumentOperation operation, int textPosition)
    {
        var hasAlignment = !string.IsNullOrWhiteSpace(operation.Alignment);
        var hasLocation = operation.LocationX.HasValue || operation.LocationY.HasValue;

        if (operation.LocationX.HasValue != operation.LocationY.HasValue)
        {
            throw new ArgumentException("Both locationX and locationY are required for located image insertion.");
        }

        if (operation.PageNumber.HasValue && !hasLocation)
        {
            throw new ArgumentException("pageNumber requires locationX and locationY.");
        }

        if (hasAlignment && hasLocation)
        {
            throw new ArgumentException("Use either alignment or location for append_image, not both.");
        }

        if (hasAlignment)
        {
            return images.Add(
                txImage,
                ResolveHorizontalAlignment(operation.Alignment!),
                textPosition,
                ResolveFloatingInsertionMode(operation.InsertionMode));
        }

        if (hasLocation)
        {
            var point = new System.Drawing.Point(
                ToTwips(operation.LocationX, operation.LocationUnit ?? operation.Unit ?? "twips"),
                ToTwips(operation.LocationY, operation.LocationUnit ?? operation.Unit ?? "twips"));
            var insertionMode = ResolveFloatingInsertionMode(operation.InsertionMode);

            if (operation.PageNumber.HasValue)
            {
                if (operation.PageNumber.Value < 1)
                {
                    throw new ArgumentException("pageNumber must be greater than 0.");
                }

                return images.Add(txImage, operation.PageNumber.Value, point, insertionMode);
            }

            return operation.TextPosition.HasValue
                ? images.Add(txImage, point, textPosition, insertionMode)
                : images.Add(txImage, point, insertionMode);
        }

        if (!string.IsNullOrWhiteSpace(operation.InsertionMode))
        {
            throw new ArgumentException("insertionMode can only be used with alignment or location. Omit insertionMode for character-like image insertion.");
        }

        return images.Add(txImage, textPosition);
    }

    private static ImageTarget ResolveImageTarget(string? target)
    {
        if (string.IsNullOrWhiteSpace(target))
        {
            return new ImageTarget("body", null);
        }

        return target.Trim().ToLowerInvariant() switch
        {
            "body" or "document" or "main" => new ImageTarget("body", null),
            "header" => new ImageTarget("header", HeaderFooterType.Header),
            "footer" => new ImageTarget("footer", HeaderFooterType.Footer),
            "firstpageheader" or "first_page_header" or "first-page-header" => new ImageTarget("firstPageHeader", HeaderFooterType.FirstPageHeader),
            "firstpagefooter" or "first_page_footer" or "first-page-footer" => new ImageTarget("firstPageFooter", HeaderFooterType.FirstPageFooter),
            "evenheader" or "even_header" or "even-header" => new ImageTarget("evenHeader", HeaderFooterType.EvenHeader),
            "evenfooter" or "even_footer" or "even-footer" => new ImageTarget("evenFooter", HeaderFooterType.EvenFooter),
            _ => throw new ArgumentException("target must be one of: body, header, footer, firstPageHeader, firstPageFooter, evenHeader, evenFooter.")
        };
    }

    private static int AddImageToModel(
        DocumentOperationContext context,
        ImageTarget target,
        string imageId,
        ImageSource imageSource,
        DocumentOperation operation)
    {
        var imageBlock = new DocumentModel.DocumentBlock
        {
            Type = "image",
            Image = new DocumentModel.Image
            {
                Id = imageId,
                Source = imageSource.ModelSource,
                AltText = operation.AltText,
                Width = operation.Width,
                Height = operation.Height,
                Unit = string.IsNullOrWhiteSpace(operation.Unit) ? "px" : operation.Unit.Trim(),
                HorizontalScaling = operation.HorizontalScaling,
                VerticalScaling = operation.VerticalScaling,
                InsertionMode = operation.InsertionMode,
                Alignment = operation.Alignment,
                LocationX = operation.LocationX,
                LocationY = operation.LocationY,
                LocationUnit = operation.LocationUnit
            }
        };

        var section = context.GetCurrentSection();
        if (!target.HeaderFooterType.HasValue)
        {
            var blockIndex = section.Blocks.Count;
            section.Blocks.Add(imageBlock);
            return blockIndex;
        }

        var headerFooter = GetOrCreateModelHeaderFooter(section, target);
        var headerFooterBlockIndex = headerFooter.Blocks.Count;
        headerFooter.Blocks.Add(imageBlock);
        return headerFooterBlockIndex;
    }

    private static DocumentModel.HeaderFooter GetOrCreateModelHeaderFooter(DocumentModel.Section section, ImageTarget target)
    {
        var isHeader = target.HeaderFooterType is HeaderFooterType.Header or HeaderFooterType.FirstPageHeader or HeaderFooterType.EvenHeader;
        var existing = isHeader ? section.Header : section.Footer;
        if (existing is not null)
        {
            return existing;
        }

        var created = new DocumentModel.HeaderFooter
        {
            Type = target.ModelName
        };

        if (isHeader)
        {
            section.Header = created;
        }
        else
        {
            section.Footer = created;
        }

        return created;
    }

    private static string GetModelLocation(DocumentOperationContext context, ImageTarget target, int blockIndex)
        => target.HeaderFooterType.HasValue
            ? $"sections[{context.CurrentSectionIndex}].{target.ModelName}.blocks[{blockIndex}].image"
            : $"sections[{context.CurrentSectionIndex}].blocks[{blockIndex}].image";

    private static HorizontalAlignment ResolveHorizontalAlignment(string alignment)
        => alignment.Trim().ToLowerInvariant() switch
        {
            "left" => HorizontalAlignment.Left,
            "right" => HorizontalAlignment.Right,
            "center" or "centered" => HorizontalAlignment.Center,
            _ => throw new ArgumentException("alignment must be one of: left, right, center, centered.")
        };

    private static ImageInsertionMode ResolveFloatingInsertionMode(string? insertionMode)
        => string.IsNullOrWhiteSpace(insertionMode)
            ? ImageInsertionMode.DisplaceText
            : insertionMode.Trim().ToLowerInvariant() switch
            {
                "abovetext" or "above_the_text" or "above-the-text" => ImageInsertionMode.AboveTheText,
                "belowtext" or "below_the_text" or "below-the-text" => ImageInsertionMode.BelowTheText,
                "displacecompletelines" or "displace_complete_lines" or "displace-complete-lines" => ImageInsertionMode.DisplaceCompleteLines,
                "displacetext" or "displace_text" or "displace-text" => ImageInsertionMode.DisplaceText,
                _ => throw new ArgumentException("insertionMode must be one of: aboveText, belowText, displaceText, displaceCompleteLines.")
            };

    private static int ToTwips(float? value, string? unit)
    {
        if (!value.HasValue)
        {
            return 0;
        }

        if (value.Value <= 0)
        {
            throw new ArgumentException("Image width and height must be greater than 0.");
        }

        var normalized = string.IsNullOrWhiteSpace(unit) ? "px" : unit.Trim().ToLowerInvariant();
        var twips = normalized switch
        {
            "twip" or "twips" => value.Value,
            "pt" or "point" or "points" => value.Value * 20f,
            "px" or "pixel" or "pixels" => value.Value * 15f,
            "in" or "inch" or "inches" => value.Value * 1440f,
            "cm" => value.Value * 1440f / 2.54f,
            "mm" => value.Value * 1440f / 25.4f,
            _ => throw new ArgumentException("Image unit must be one of: px, pt, in, cm, mm, twips.")
        };

        return (int)Math.Round(twips);
    }

    private sealed class ImageSource : IDisposable
    {
        private readonly MemoryStream? _memoryStream;

        private ImageSource(
            string modelSource,
            string displaySource,
            string defaultName,
            int? filterIndex,
            MemoryStream? memoryStream)
        {
            ModelSource = modelSource;
            DisplaySource = displaySource;
            DefaultName = defaultName;
            FilterIndex = filterIndex;
            _memoryStream = memoryStream;
        }

        public string ModelSource { get; }
        public string DisplaySource { get; }
        public string DefaultName { get; }
        private int? FilterIndex { get; }

        public static ImageSource FromPath(string imagePath, int filterIndex)
            => new(
                imagePath,
                imagePath,
                Path.GetFileNameWithoutExtension(imagePath),
                filterIndex,
                null);

        public static ImageSource FromBase64(byte[] bytes, string? format)
        {
            var memoryStream = new MemoryStream(bytes, 0, bytes.Length, writable: false, publiclyVisible: true);
            var normalizedFormat = string.IsNullOrWhiteSpace(format)
                ? "base64"
                : format.Trim().TrimStart('.').ToLowerInvariant();

            return new(
                $"data:image/{normalizedFormat};base64,...",
                $"base64 {normalizedFormat} image",
                "embedded-image",
                null,
                memoryStream);
        }

        public TxImage CreateImage()
        {
            if (_memoryStream is not null)
            {
                _memoryStream.Position = 0;
                return new TxImage(_memoryStream);
            }

            return new TxImage(ModelSource, FilterIndex!.Value);
        }

        public void Dispose()
            => _memoryStream?.Dispose();
    }

    private sealed record ImageTarget(string ModelName, HeaderFooterType? HeaderFooterType);
}

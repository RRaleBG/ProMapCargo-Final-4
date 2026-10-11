# Bookmarks

> All bookmark operations — creating bookmarks, navigating to bookmarks, retrieving content, inserting content, replacing content, and deleting bookmarks.

---

## Required Common usings

```csharp
using Syncfusion.DocIO;
using Syncfusion.DocIO.DLS;
```

## Required usings for Windows-Specific

```csharp
using System;
using System.IO;
```

## Add Bookmark

### Minimal Code
```csharp
var para = section.AddParagraph();
para.AppendBookmarkStart("BookmarkName");
para.AppendText("Content inside bookmark");
para.AppendBookmarkEnd("BookmarkName");
```

### With Formatted Content
```csharp
var para = section.AddParagraph();
para.AppendBookmarkStart("Northwind");
var text = para.AppendText("The Northwind sample database provides data you can experiment with and database objects that demonstrate features you might want to implement in your own databases.");
text.CharacterFormat.Bold = true;
text.CharacterFormat.FontSize = 12f;
para.AppendBookmarkEnd("Northwind");
para.AppendText(" Using Northwind, you can become familiar with how a relational database is structured.");
```

### Placeholders
- `"BookmarkName"` → Replace with `"{bookmark-name}"`
- `"Content inside bookmark"` → Replace with `"{bookmark-content}"`

---

## Get Bookmark Instance

### Minimal Code
```csharp
// Access a bookmark by name
var bookmark = doc.Bookmarks.FindByName("BookmarkName");
```

### Access Bookmark Properties

#### Common code for Cross-Platform and Windows-Specific
```csharp
var bookmark = doc.Bookmarks.FindByName("Northwind");
// Access the paragraph containing bookmark start
var ownerPara = bookmark.BookmarkStart.OwnerParagraph;
```

#### Cross-Platform
```csharp
ownerPara.ParagraphFormat.BackColor = Syncfusion.Drawing.Color.AliceBlue;
```

#### Windows-Specific
```csharp
ownerPara.ParagraphFormat.BackColor = System.Drawing.Color.AliceBlue;
```

### Placeholders
- `"BookmarkName"` → Replace with `"{bookmark-name}"`

---

## Remove Bookmark

### Minimal Code
```csharp
// Find and remove a bookmark
var bookmark = doc.Bookmarks.FindByName("BookmarkName");
doc.Bookmarks.Remove(bookmark);
```

### Remove by Name
```csharp
// Remove bookmark directly by name
var bookmark = doc.Bookmarks.FindByName("Northwind");
if (bookmark != null)
    doc.Bookmarks.Remove(bookmark);
```

### Remove all Bookmarks
```csharp
// Remove all the bookmarks from Word document
doc.Bookmarks.Clear();
```

### Placeholders
- `"BookmarkName"` → Replace with `"{bookmark-name}"`

---

## Navigate to Bookmark

### Minimal Code
```csharp
// Create bookmark navigator and move to bookmark
var bookmarkNavigator = new BookmarksNavigator(doc);
bookmarkNavigator.MoveToBookmark("BookmarkName");
```

### Navigate with Position Control
```csharp
var bookmarkNavigator = new BookmarksNavigator(doc);
// Move to bookmark end
bookmarkNavigator.MoveToBookmark("BookmarkName", false, true);
// Move to bookmark start
bookmarkNavigator.MoveToBookmark("BookmarkName", true, false);
```

### Placeholders
- `"BookmarkName"` → Replace with `"{bookmark-name}"`

---

## Retrieve Bookmark Content

### Get Content as TextBodyPart (Single Section)
```csharp
var bookmarkNavigator = new BookmarksNavigator(doc);
bookmarkNavigator.MoveToBookmark("BookmarkName");
// Get bookmark content as TextBodyPart
TextBodyPart part = bookmarkNavigator.GetBookmarkContent();

// Add retrieved content to another section
doc.AddSection();
for (int i = 0; i < part.BodyItems.Count; i++)
    doc.LastSection.Body.ChildEntities.Add(part.BodyItems[i]);
```

### Get Content as WordDocumentPart (Multi-Section)
```csharp
var bookmarkNavigator = new BookmarksNavigator(doc);
bookmarkNavigator.MoveToBookmark("BookmarkName");
// Get bookmark content as WordDocumentPart
WordDocumentPart wordDocumentPart = bookmarkNavigator.GetContent();

// Save as separate Word document
WordDocument newDocument = wordDocumentPart.GetAsWordDocument();
newDocument.Save(outputPath);
newDocument.Close();
wordDocumentPart.Close();
```

### Placeholders
- `"BookmarkName"` → Replace with `"{bookmark-name}"`

---

## Retrieve Word Document Content

### Get Entire Word Document Content as WordDocumentPart
```csharp
var fileStream = new FileStream("Template.docx", FileMode.Open, FileAccess.Read);
var doc = new WordDocument(fileStream, FormatType.Docx);
// Get Word document content as WordDocumentPart
WordDocumentPart wordDocumentPart = new WordDocumentPart(doc);
```

### Placeholders
- `"Template.docx"` → Replace with `"{filename}.docx"`

---

## Retrieve Bookmark Content Within Table

### Minimal Code
```csharp
var bookmarkNavigator = new BookmarksNavigator(doc);
bookmarkNavigator.MoveToBookmark("BkmkInTable");
// Set column range for rectangular selection
bookmarkNavigator.CurrentBookmark.FirstColumn = 1;
bookmarkNavigator.CurrentBookmark.LastColumn = 3;
// Get the selected content
TextBodyPart part = bookmarkNavigator.GetBookmarkContent();
```

### Full Example with Table
```csharp
var bookmarkNavigator = new BookmarksNavigator(doc);
bookmarkNavigator.MoveToBookmark("BkmkInTable");
// Select from column index 1 to 4
bookmarkNavigator.CurrentBookmark.FirstColumn = 1;
bookmarkNavigator.CurrentBookmark.LastColumn = 4;
// Retrieve content
TextBodyPart part = bookmarkNavigator.GetBookmarkContent();
// Add to new section
doc.AddSection();
for (int i = 0; i < part.BodyItems.Count; i++)
    doc.LastSection.Body.ChildEntities.Add(part.BodyItems[i]);
```

### Placeholders
- `"BkmkInTable"` → Replace with `"{bookmark-name}"`
- `1`, `3` → Replace with `{start-column}`, `{end-column}`

---

## Insert Content into Bookmark

### Insert Simple Text

#### Insert with Formatting
```csharp
var bookmarkNavigator = new BookmarksNavigator(doc);
bookmarkNavigator.MoveToBookmark("BookmarkName");
// Insert text before bookmark end and preserve existing formatting.
bookmarkNavigator.InsertText("New text content here.", true);
```

#### Insert without Formatting
```csharp
var bookmarkNavigator = new BookmarksNavigator(doc);
bookmarkNavigator.MoveToBookmark("BookmarkName");
// Insert text before bookmark end and discard existing formatting.
bookmarkNavigator.InsertText("New text content here.", false);
```

### Insert Paragraph
```csharp
var bookmarkNavigator = new BookmarksNavigator(doc);
bookmarkNavigator.MoveToBookmark("BookmarkName", false, true);
// Create new paragraph
IWParagraph paragraph = new WParagraph(doc);
paragraph.AppendText("This is a new paragraph inserted at the bookmark location.");
// Insert paragraph after bookmark start
bookmarkNavigator.InsertParagraph(paragraph);
```

### Insert Paragraph Item (Image)

#### Common code for Cross-Platform and Windows-Specific
```csharp
var bookmarkNavigator = new BookmarksNavigator(doc);
bookmarkNavigator.MoveToBookmark("BookmarkName", false, true);
// Insert picture after bookmark end
WPicture picture = bookmarkNavigator.InsertParagraphItem(ParagraphItemType.Picture) as WPicture;
```

#### Cross-Platform
```csharp
FileStream imageStream = new FileStream("image.png", FileMode.Open, FileAccess.Read);
picture.LoadImage(imageStream);
```

#### Windows-Specific
```csharp
picture.LoadImage(Image.FromFile("Northwind.png"));
```

#### Common code for Cross-Platform and Windows-Specific
```csharp
picture.WidthScale = 50;
picture.HeightScale = 50;
```

### Insert Table
```csharp
var bookmarkNavigator = new BookmarksNavigator(doc);
bookmarkNavigator.MoveToBookmark("BookmarkName", false, false);
// Create and insert table
WTable table = new WTable(doc);
table.ResetCells(3, 2);
table[0, 0].AddParagraph().AppendText("Column 1");
table[0, 1].AddParagraph().AppendText("Column 2");
table[1, 0].AddParagraph().AppendText("Data 1");
table[1, 1].AddParagraph().AppendText("Data 2");
table[2, 0].AddParagraph().AppendText("Data 3");
table[2, 1].AddParagraph().AppendText("Data 4");
bookmarkNavigator.InsertTable(table);
```

### Insert TextBodyPart
```csharp
var bookmarkNavigator = new BookmarksNavigator(doc);
bookmarkNavigator.MoveToBookmark("SourceBookmark");
// Get content from source bookmark
TextBodyPart textBodyPart = bookmarkNavigator.GetBookmarkContent();

// Move to destination bookmark
bookmarkNavigator.MoveToBookmark("DestinationBookmark", true, true);
// Insert the text body part
bookmarkNavigator.InsertTextBodyPart(textBodyPart);
```

### Placeholders
- `"BookmarkName"` → Replace with `"{bookmark-name}"`
- `"New text content here."` → Replace with `"{text-content}"`

---

## Delete Bookmark Content

### Minimal Code
```csharp
var bookmarkNavigator = new BookmarksNavigator(doc);
bookmarkNavigator.MoveToBookmark("BookmarkName");
// Delete content but preserve formatting
bookmarkNavigator.DeleteBookmarkContent(false);
```

### Delete with Formatting
```csharp
var bookmarkNavigator = new BookmarksNavigator(doc);
bookmarkNavigator.MoveToBookmark("BookmarkName");
// Delete content including formatting
bookmarkNavigator.DeleteBookmarkContent(true);
```

### Placeholders
- `"BookmarkName"` → Replace with `"{bookmark-name}"`

---

## Replace Bookmark Content

### Replace with TextBodyPart
```csharp
var bookmarkNavigator = new BookmarksNavigator(doc);
bookmarkNavigator.MoveToBookmark("SourceBookmark");
// Get content from source
TextBodyPart textBodyPart = bookmarkNavigator.GetBookmarkContent();

// Move to target bookmark
bookmarkNavigator.MoveToBookmark("TargetBookmark");
// Replace content
bookmarkNavigator.ReplaceBookmarkContent(textBodyPart);
```

### Replace with Plain Text

#### Replace with Formatting
```csharp
// Bookmark "BookmarkName" already exists and contains formatted text
BookmarksNavigator bookmarkNavigator = new BookmarksNavigator(doc);
// Move to the virtual cursor before the end location of the bookmark "BookmarkName"
bookmarkNavigator.MoveToBookmark("BookmarkName");
// Replace the bookmark content with simple text and preserve existing formatting.
bookmarkNavigator.ReplaceBookmarkContent(" Northwind Database is a set of tables containing data fitted into predefined categories.", true);
```

#### Replace without Formatting
```csharp
// Bookmark "BookmarkName" already exists and contains formatted text
BookmarksNavigator bookmarkNavigator = new BookmarksNavigator(doc);
// Move to the virtual cursor before the end location of the bookmark "BookmarkName"
bookmarkNavigator.MoveToBookmark("BookmarkName");
// Replace the bookmark content with simple text and discard existing formatting.
bookmarkNavigator.ReplaceBookmarkContent(" Northwind Database is a set of tables containing data fitted into predefined categories.", false);
```

#### Placeholders
- `"BookmarkName"` → Replace with `"{bookmark-name}"`

### Replace with WordDocumentPart

#### Common for Cross-Platform and Windows-Specific
```csharp
// Load template document
FileStream templateStream = new FileStream("Template.docx", FileMode.Open, FileAccess.Read);
WordDocument templateDoc = new WordDocument(templateStream, FormatType.Docx);
var bookmarkNavigator = new BookmarksNavigator(templateDoc);
bookmarkNavigator.MoveToBookmark("SourceBookmark");
// Get content as WordDocumentPart
WordDocumentPart wordDocumentPart = bookmarkNavigator.GetContent();

// Load target document
FileStream targetStream = new FileStream("Target.docx", FileMode.Open, FileAccess.Read);
WordDocument targetDoc = new WordDocument(targetStream, FormatType.Docx);
bookmarkNavigator = new BookmarksNavigator(targetDoc);
bookmarkNavigator.MoveToBookmark("TargetBookmark");
// Replace content
bookmarkNavigator.ReplaceContent(wordDocumentPart);

wordDocumentPart.Close();
templateDoc.Close();
```

### Placeholders
- `"SourceBookmark"`, `"TargetBookmark"` → Replace with `"{source-bookmark-name}"`, `"{target-bookmark-name}"`

---

## Complete Example: Bookmark Operations

### Full Example

#### Common for Cross-Platform and Windows-Specific
```csharp
var outputPath = Path.Combine(Directory.GetCurrentDirectory(), "output", "BookmarkOperations.docx");
var doc = new WordDocument();
var section = doc.AddSection();
section.PageSetup.Margins.All = 72f;

// Add title
var title = section.AddParagraph();
title.AppendText("Bookmark Operations Demo");
title.ApplyStyle(BuiltinStyle.Heading1);
section.AddParagraph();

// Add first paragraph with bookmark
var para1 = section.AddParagraph();
para1.AppendText("Before bookmark. ");
para1.AppendBookmarkStart("ContentBookmark");
para1.AppendText("This content is inside the bookmark and can be retrieved, replaced, or deleted.");
para1.AppendBookmarkEnd("ContentBookmark");
para1.AppendText(" After bookmark.");
section.AddParagraph();

// Navigate to bookmark and insert content
var bookmarkNavigator = new BookmarksNavigator(doc);
bookmarkNavigator.MoveToBookmark("ContentBookmark", false, true);
bookmarkNavigator.InsertText(" [Inserted text after bookmark start]", false);

// Add another section with bookmark
doc.AddSection();
var para2 = doc.LastSection.AddParagraph();
para2.AppendText("Target section with empty bookmark: ");
para2.AppendBookmarkStart("EmptyBookmark");
para2.AppendBookmarkEnd("EmptyBookmark");

// Copy content from first bookmark to second
bookmarkNavigator.MoveToBookmark("ContentBookmark");
TextBodyPart contentPart = bookmarkNavigator.GetBookmarkContent();
bookmarkNavigator.MoveToBookmark("EmptyBookmark");
bookmarkNavigator.ReplaceBookmarkContent(contentPart);

doc.Save(outputPath);
doc.Close();
Console.WriteLine($"SUCCESS: {outputPath}");
```

﻿using System;
using Eto.Forms;
using Eto.Drawing;
using Eto.Mac.Drawing;
using System.IO;
using System.Collections.Generic;
using System.Text;

#if XAMMAC2
using AppKit;
using Foundation;
using CoreGraphics;
using ObjCRuntime;
using CoreAnimation;
using CoreImage;
using nnuint = System.Int32;
#else
using MonoMac.AppKit;
using MonoMac.Foundation;
using MonoMac.CoreGraphics;
using MonoMac.ObjCRuntime;
using MonoMac.CoreAnimation;
using MonoMac.CoreImage;
#if Mac64
using CGSize = MonoMac.Foundation.NSSize;
using CGRect = MonoMac.Foundation.NSRect;
using CGPoint = MonoMac.Foundation.NSPoint;
using nfloat = System.Double;
using nint = System.Int64;
using nuint = System.UInt64;
using nnuint = System.UInt64;
#else
using CGSize = System.Drawing.SizeF;
using CGRect = System.Drawing.RectangleF;
using CGPoint = System.Drawing.PointF;
using nfloat = System.Single;
using nint = System.Int32;
using nuint = System.UInt32;
using nnuint = System.Int32;
#endif
#endif

namespace Eto.Mac.Forms.Controls
{
	public class RichTextAreaHandler : TextAreaHandler<RichTextArea, RichTextArea.ICallback>, RichTextArea.IHandler, ITextBuffer
	{
		public RichTextAreaHandler()
		{
			Control.RichText = true;
		}

		T GetSelectedTextAttribute<T>(NSString attributeName)
			where T: class
		{
			NSRange effectiveRange;
			var attrib = Control.SelectedRange.Length == 0 ? Control.TypingAttributes : Control.TextStorage.GetAttributes((nnuint)Math.Min(Control.SelectedRange.Location, Control.TextStorage.Length - 1), out effectiveRange);
			NSObject value;
			return attrib.TryGetValue(attributeName, out value) ? value as T : null;
		}

		public void SetFont(Range<int> range, Font font)
		{
			var nsrange = range.ToNS();
			Control.SetFont(font.ToNS(), nsrange);
			var attr = font.Attributes();
			if (attr != null && attr.Count > 0)
				Control.TextStorage.AddAttributes(attr, nsrange);
		}

		public void SetFamily(Range<int> range, FontFamily family)
		{
			var familyName = ((FontFamilyHandler)family.Handler).MacName;
			SetFontAttribute(range.ToNS(), true, font => NSFontManager.SharedFontManager.ConvertFontToFamily(font, familyName));
		}

		public void SetForeground(Range<int> range, Color color)
		{
			Control.TextStorage.AddAttribute(NSStringAttributeKey.ForegroundColor, color.ToNSUI(), range.ToNS());
		}

		public void SetBackground(Range<int> range, Color color)
		{
			Control.TextStorage.AddAttribute(NSStringAttributeKey.BackgroundColor, color.ToNSUI(), range.ToNS());
		}

		public void SetBold(Range<int> range, bool bold)
		{
			SetFontAttribute(range.ToNS(), NSFontTraitMask.Bold, NSFontTraitMask.Unbold, bold);
		}

		public void SetItalic(Range<int> range, bool italic)
		{
			SetFontAttribute(range.ToNS(), NSFontTraitMask.Italic, NSFontTraitMask.Unitalic, italic);
		}

		public void SetUnderline(Range<int> range, bool underline)
		{
			Control.TextStorage.AddAttribute(NSStringAttributeKey.UnderlineStyle, new NSNumber((int)(underline ? NSUnderlineStyle.Single : NSUnderlineStyle.None)), range.ToNS());
		}

		public void SetStrikethrough(Range<int> range, bool strikethrough)
		{
			Control.TextStorage.AddAttribute(NSStringAttributeKey.StrikethroughStyle, new NSNumber((int)(strikethrough ? NSUnderlineStyle.Single : NSUnderlineStyle.None)), range.ToNS());
		}

		bool HasFontAttribute(NSFontTraitMask traitMask)
		{
			NSRange effectiveRange;
			var attrib = Control.SelectedRange.Length == 0 ? Control.TypingAttributes : Control.TextStorage.GetAttributes((nnuint)Math.Min(Control.SelectedRange.Location, Control.TextStorage.Length - 1), out effectiveRange, Control.SelectedRange);
			NSObject value;
			if (attrib.TryGetValue(NSStringAttributeKey.Font, out value))
			{
				var font = value as NSFont;
				if (font != null)
				{
					var traits = NSFontManager.SharedFontManager.TraitsOfFont(font);
					return traits.HasFlag(traitMask);
				}
			}
			return false;
		}

		void SetSelectedFontAttribute(NSFontTraitMask traitMask, NSFontTraitMask traitUnmask, bool enabled)
		{
			SetSelectedFontAttribute(enabled, f => UpdateFontTrait(f, traitMask, traitUnmask, enabled));
		}

		void SetSelectedFontAttribute(bool enabled, Func<NSFont, NSFont> updateFont)
		{
			var range = Control.SelectedRange;
			if (range.Length > 0)
				SetFontAttribute(range, enabled, updateFont);
			else
				Control.TypingAttributes = UpdateFontAttributes(Control.TypingAttributes, enabled, updateFont);
		}

		void SetSelectedAttribute(NSString attribute, NSObject value)
		{
			var range = Control.SelectedRange;
			if (range.Length > 0)
				Control.TextStorage.AddAttribute(attribute, value, range);
			else
			{
				var mutableAttributes = new NSMutableDictionary(Control.TypingAttributes);
				mutableAttributes.SetValueForKey(value, attribute);
				Control.TypingAttributes = mutableAttributes;
			}
		}

		void SetFontAttribute(NSRange range, NSFontTraitMask traitMask, NSFontTraitMask traitUnmask, bool enabled)
		{
			SetFontAttribute(range, enabled, f => UpdateFontTrait(f, traitMask, traitUnmask, enabled));
		}

		void SetFontAttribute(NSRange range, bool enabled, Func<NSFont, NSFont> updateFont)
		{
			NSRange effectiveRange;
			if (Control.ShouldChangeTextNew(range, null))
			{
				Control.TextStorage.BeginEditing();
				var current = range;
				var left = current.Length;
				do
				{
					var attribs = Control.TextStorage.GetAttributes(current.Location, out effectiveRange, current);
					attribs = UpdateFontAttributes(attribs, enabled, updateFont);
					var span = effectiveRange.Location + effectiveRange.Length - current.Location;
					var newRange = new NSRange(current.Location, (nnuint)Math.Min(span, current.Length));
					Control.TextStorage.AddAttributes(attribs, newRange);
					current.Location += span;
					current.Length -= span;
					left -= span;
				} while (left > 0);
				Control.TextStorage.EndEditing();
				Control.DidChangeText();
			}
		}

		NSFont UpdateFontTrait(NSFont font, NSFontTraitMask traitMask, NSFontTraitMask traitUnmask, bool enabled)
		{
			var traits = enabled ? traitMask : traitUnmask;
			return NSFontManager.SharedFontManager.ConvertFont(font, traits);
		}

		NSDictionary UpdateFontAttributes(NSDictionary attribs, bool enabled, Func<NSFont, NSFont> updateFont)
		{
			NSObject fontValue = null;
			if ((enabled && attribs == null)
				|| (attribs != null && attribs.TryGetValue(NSStringAttributeKey.Font, out fontValue)))
			{
				var font = fontValue as NSFont ?? Control.Font;

				font = updateFont(font);

				var mutableAttribs = new NSMutableDictionary(attribs);
				mutableAttribs.SetValueForKey(font, NSStringAttributeKey.Font);
				return mutableAttribs;
			}
			return attribs;
		}

		public Font SelectionFont
		{
			get { return GetSelectedTextAttribute<NSFont>(NSStringAttributeKey.Font).ToEto(); }
			set { SetSelectedAttribute(NSStringAttributeKey.Font, value.ToNS()); }
		}

		public Color SelectionForeground
		{
			get { return GetSelectedTextAttribute<NSColor>(NSStringAttributeKey.ForegroundColor).ToEto(); }
			set { SetSelectedAttribute(NSStringAttributeKey.ForegroundColor, value.ToNSUI()); }
		}

		public Color SelectionBackground
		{
			get { return GetSelectedTextAttribute<NSColor>(NSStringAttributeKey.BackgroundColor).ToEto(); }
			set { SetSelectedAttribute(NSStringAttributeKey.BackgroundColor, value.ToNSUI()); }
		}

		public bool SelectionBold
		{
			get { return HasFontAttribute(NSFontTraitMask.Bold); }
			set { SetSelectedFontAttribute(NSFontTraitMask.Bold, NSFontTraitMask.Unbold, value); }
		}

		public bool SelectionItalic
		{
			get { return HasFontAttribute(NSFontTraitMask.Italic); }
			set { SetSelectedFontAttribute(NSFontTraitMask.Italic, NSFontTraitMask.Unitalic, value); }
		}

		public bool SelectionUnderline
		{
			get
			{
				var value = GetSelectedTextAttribute<NSNumber>(NSStringAttributeKey.UnderlineStyle);
				return value != null && value.Int32Value != (int)NSUnderlineStyle.None;
			}
			set { SetSelectedAttribute(NSStringAttributeKey.UnderlineStyle, new NSNumber((int)(value ? NSUnderlineStyle.Single : NSUnderlineStyle.None))); }
		}

		public bool SelectionStrikethrough
		{
			get
			{
				var value = GetSelectedTextAttribute<NSNumber>(NSStringAttributeKey.StrikethroughStyle);
				return value != null && value.Int32Value != (int)NSUnderlineStyle.None;
			}
			set { SetSelectedAttribute(NSStringAttributeKey.StrikethroughStyle, new NSNumber((int)(value ? NSUnderlineStyle.Single : NSUnderlineStyle.None))); }
		}

		public FontFamily SelectionFamily
		{
			get { return GetSelectedTextAttribute<NSFont>(NSStringAttributeKey.Font).ToEto().Family; }
			set
			{
				var familyName = ((FontFamilyHandler)value.Handler).MacName;
				SetSelectedFontAttribute(true, f => NSFontManager.SharedFontManager.ConvertFontToFamily(f, familyName));
			}
		}

		public void Clear()
		{
			Control.TextStorage.SetString(new NSAttributedString());
		}

		public void Delete(Range<int> range)
		{
			Control.TextStorage.DeleteRange(range.ToNS());
		}

		public void Insert(int position, string text)
		{
			Control.TextStorage.Insert(new NSAttributedString(text), (nnuint)position);
		}

		public IEnumerable<RichTextAreaFormat> SupportedFormats
		{
			get
			{
				yield return RichTextAreaFormat.Rtf;
				yield return RichTextAreaFormat.PlainText;
			}
		}

		public void Load(Stream stream, RichTextAreaFormat format)
		{
			switch (format)
			{
				case RichTextAreaFormat.Rtf:
					var range = new NSRange(0, Control.TextStorage.Length);
					Control.ReplaceWithRtf(range, NSData.FromStream(stream));
					break;
				case RichTextAreaFormat.PlainText:
					Control.TextStorage.SetString(new NSAttributedString(new StreamReader(stream).ReadToEnd()));
					break;
				default:
					throw new NotSupportedException();
			}
		}

		public void Save(Stream stream, RichTextAreaFormat format)
		{
			switch (format)
			{
				case RichTextAreaFormat.Rtf:
					var range = new NSRange(0, Control.TextStorage.Length);
					Control.RtfFromRange(range).AsStream().CopyTo(stream);
					break;
				case RichTextAreaFormat.PlainText:
					var bytes = Encoding.UTF8.GetBytes(Control.TextStorage.Value);
					stream.Write(bytes, 0, bytes.Length);
					break;
				default:
					throw new NotSupportedException();
			}
		}

		public ITextBuffer Buffer
		{
			get { return this; }
		}
	}
}


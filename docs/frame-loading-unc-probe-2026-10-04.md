# Frame loading probes UNC paths

**Status:** open, not fixed. Found on 2026-10-04 while investigating the reCAPTCHA demo page.
**Components:** Broiler.Layout (the defect), Broiler.HTML (a smaller defect of the same kind).
**Affects:** the Broiler.Browser window and `Broiler.Cli --analyze`, on Windows.

## Summary

Broiler.Layout loads a frame's document itself when the script bridge has not stamped the frame's
live document onto it. It does this by treating the frame's `src` (or an `<object>`'s `data`) as a
path on the local file system. It does so whatever the containing document is, including an
`http(s)` page.

A protocol-relative URL (`//host/path`) turns into a UNC path (`\\host\path`) on the way. Asking
whether that file exists makes Windows contact the named host over SMB.

Web content must never make the renderer touch the local file system or open a network share.
Broiler.HTML already enforces this for a web page's stylesheets, images and fonts
(`SubresourceScope.AllowsLocalFiles`). Frames are not covered.

## Where

- `Broiler.Layout/IR/FragmentTreeBuilder.cs`, `TryLoadEmbeddedDocument`. Line numbers are from
  origin/main 9924d7d, which Broiler.Layout 0.1.0-preview.10 was built from.
  - The document-relative branch (lines 788–823) joins the URL onto the containing document's
    directory:
    - `ResolveRelativeDocumentPath` turns `//host/a/b.html` into `\\host\a\b.html`.
    - `Path.Combine` returns that rooted path unchanged.
    - `Path.GetFullPath` and `File.Exists` then probe it.
  - Nothing checks first whether the containing document is a `file:` document.
- It is called from `FragmentTreeBuilder.Build` (line 344) for every visible frame box. That
  happens on every layout: on the window's UI thread, and in the CLI's render.
- Broiler.HTML, `Source/Broiler.HTML.Orchestration/HtmlContainerInt.cs`,
  `ResolveLocalFontPath` (line 945 at 300670d / 0.1.0-preview.10). It runs
  `Path.IsPathRooted(src) && File.Exists(src)` on `@font-face` sources. This affects containers
  with no document identity and no transport: `HtmlRender`'s static API, and the frames that
  Broiler.Cli's `RenderProbe` composites through it. For a web page's own container the existing
  `AllowsLocalFiles` check already skips it.

## Observed

- An HTTP page holding a single `<iframe>` with a protocol-relative `src` took 22.6 s in the
  `--analyze` render phase. `--sample-stacks` showed the time in `System.IO.File.Exists`, called
  from `FragmentTreeBuilder.TryLoadEmbeddedDocument`.
- `--analyze https://www.google.com/recaptcha/api2/demo` spends about 23 s rendering for the same
  reason in Broiler.HTML. The reCAPTCHA frame's `@font-face` rules use `//fonts.gstatic.com/...`,
  and the CLI renders that frame through `HtmlRender`, without a document identity.
- The window phase that ran after the first test took 0.3 s, most likely because Windows caches
  the failed lookup. A window in a fresh process runs the same layout code and is expected to
  stall the same way; that has not been measured.

## Impact

- **Stall.** Several seconds per probe, during layout. In the browser that means on the UI
  thread.
- **Unintended network access.** On Windows, probing a UNC path opens an SMB connection to the
  named host, which can authenticate as the signed-in user. A page, not the user, picks the host.
- **Local files.** For a web page, the same branch can read a local file into a frame's
  rendering. The subresource policy forbids this for every other kind of resource.

## Expected behaviour

Frames should follow the rule `SubresourceScope.AllowsLocalFiles` already applies:

- Only a `file:` document, or a tool rendering local markup with no document identity, may load
  a frame from the file system.
- A path that names another host (UNC, or protocol-relative) is never probed.
- The decision rests on the document's URL, not its base URL, because a `<base>` element changes
  the base URL.
- The base URL carried with a frame's live document (`data-broiler-frame-base`) is read from
  markup, so the same policy applies to it.

These existing behaviours must keep working:
- file-based renders, which is how the WPT runner works;
- root-relative loads from `DocumentRoot`;
- `DocumentRoot.TryStripLocalOrigin`, which serves WPT's other hosts from one checkout.

## Suggested fix

1. **Broiler.Layout.** In `TryLoadEmbeddedDocument`, never resolve a protocol-relative or UNC
   source to a path. Skip the file-system branches unless the containing document is a `file:`
   document.
   - Broiler.HTML is the component that knows the document's identity (`AllowsLocalFiles`). So
     the cleanest shape is probably for the host to pass that decision down to the fragment
     builder, as `DocumentRoot` is passed today.
2. **Broiler.HTML.** `ResolveLocalFontPath` should skip UNC and protocol-relative sources.
   Separately, consider treating a container whose base URL is `http(s)` as web content even
   when it has no transport. `HtmlRender` and the CLI's frame compositing are such containers.
3. **Broiler.Browser.** Once the packages are published, bump the component pins in
   `Broiler.Browser.Core`.

## Tests to add

- **Broiler.Layout:**
  - a frame box in an `http(s)` document with a protocol-relative `src` returns no document and
    touches no file;
  - a document-relative `src` whose resolved path exists on disk is not read for a web document;
  - the existing file-based and WPT frame tests keep passing.
- **Broiler.HTML:** a `//host/...` `@font-face` source is not probed by a container without a
  document identity.
- For "touches no file", a seam over the file-system calls works better than timing a probe.
  Each new test should fail without its fix.

## Related

- The window's frame painting (`FrameCompositor`, Broiler.Browser #162) refuses to paint a web
  page's frame whose document URL is a `file:` URL. It cannot prevent this probe, which happens
  earlier, in layout.

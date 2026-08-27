# Third-party notices

Broiler is an independent project. References to upstream projects identify the origin
of inherited code and ideas; they do not imply affiliation, sponsorship, endorsement,
or responsibility for Broiler by the upstream authors.

## HTML Renderer

Broiler.HTML is derived in part from [HTML Renderer](https://github.com/ArthurHub/HTML-Renderer),
created by José Manuel Menéndez Poo and developed by Arthur Teplitzki and other
contributors. HTML Renderer is licensed under the BSD 3-Clause License. Broiler retains
its copyright and license conditions in
[`LICENSES/HTML-Renderer-BSD-3-Clause.txt`](LICENSES/HTML-Renderer-BSD-3-Clause.txt).

Broiler.HTML has diverged substantially and is maintained independently. “HTML Renderer”
is used here only to describe provenance. The upstream authors have not reviewed or
endorsed Broiler.

## Yantra JS

Broiler.JS is derived in part from [Yantra JS](https://github.com/yantrajs/yantra) and
retains the contribution history and attribution of that project. Yantra JS is licensed
under the Apache License 2.0, a copy of which is included in [`LICENSE`](LICENSE).

Broiler.JS has diverged substantially and is maintained independently. “Yantra JS” is
used here only to describe provenance. The Yantra JS authors have not reviewed or
endorsed Broiler.

## Unicode data

Broiler.Unicode contains generated tables based on Unicode and CLDR data. Those data
files remain subject to the [Unicode Terms of Use](https://www.unicode.org/terms_of_use.html),
as identified in that component's documentation and notices.

## Jint

The Octane benchmark harness runs one of its reference engines on
[Jint](https://github.com/sebastienros/jint), an independent managed ECMAScript
interpreter by Sébastien Ros and contributors, licensed under the BSD 2-Clause License.
It is consumed as an unmodified NuGet package by
[`tests/octane/jint-host`](tests/octane/jint-host), a benchmark tool that is not part of
any shipped Broiler component and carries no Jint code. Jint is used here only as a
measurement reference; its authors have not reviewed or endorsed Broiler.

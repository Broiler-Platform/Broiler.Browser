# When a script navigates

**This document moved.** It describes the DOM bridge's navigation seam — `NavigationRequest`,
`TakePendingNavigation`, and the split between a binding that can only *say* where the page
wanted to go and a host that decides whether to follow. All of that left this repository with
the bridge in September 2026.

It now lives in the Broiler.HtmlBridge component:

- in this checkout: [`Broiler.HtmlBridge/docs/script-initiated-navigation.md`](../Broiler.HtmlBridge/docs/script-initiated-navigation.md)
- upstream: <https://github.com/Broiler-Platform/Broiler.HtmlBridge/blob/main/docs/script-initiated-navigation.md>

The half that stayed here is the half it calls "what is still not wired": which requests this
browser follows, and what it does with the ones it does not. That is `Broiler.Browser.Core`'s
decision and is documented with the code that makes it.

# Security policy

## Reporting a vulnerability

Please **don't** open a public issue for security problems.

Report it privately through GitHub: open the repository's **Security** tab and choose **Report a vulnerability**.
Include what you found, how to reproduce it and the Cluster version (right-click `Cluster.exe` → Properties → Details).

You should get a reply within a week. Fixes ship as a new release; the release notes credit the reporter unless
you'd rather stay anonymous.

## Supported versions

Only the latest release gets fixes.

## Scope

Cluster runs as the signed-in user with no admin rights and makes no network connections. Relevant reports include:
anything that makes it run code it shouldn't, write outside `%LOCALAPPDATA%\Cluster` and the user's `Run` key,
or open a network connection.

Release downloads are not code-signed. Verify a download against the `.sha256` file published with each release.

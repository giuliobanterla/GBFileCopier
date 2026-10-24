# SignPath configuration

The release workflow signs the Windows files through [SignPath](https://signpath.io), with the free
certificate of the SignPath Foundation for open source projects. It does so only when the repository
has the variables `SIGNPATH_ORGANIZATION_ID` and `SIGNPATH_PROJECT_SLUG` and the secret
`SIGNPATH_API_TOKEN`; otherwise the files stay unsigned.

In the SignPath project (slug as in `SIGNPATH_PROJECT_SLUG`):

- Trusted build system: the predefined **GitHub.com** one, linked to the project.
- Signing policies: `release-signing` (SignPath Foundation certificate, manual approval) and
  `test-signing` (test certificate). Tags use the first, manual runs of the workflow the second.
- Artifact configurations, with these slugs and contents:
  - `program-files`: [program-files.xml](program-files.xml) — the two programs, before they go into
    the installer and the zip;
  - `installer`: [installer.xml](installer.xml) — the installer.

The libraries shipped with the app (SkiaSharp, HarfBuzzSharp, ANGLE) come from other projects: per
the SignPath Foundation terms they are not signed with this certificate.

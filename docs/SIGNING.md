# Code signing

Unsigned `.exe` files make Windows SmartScreen warn users ("Windows protected your PC").
Kenma uses the **SignPath Foundation**, which signs open-source projects for free with a certificate issued to the Foundation.

The release workflow (`.github/workflows/release.yml`) is already wired up. It signs automatically once the
repository settings below exist, and builds unsigned releases until then.

## 1. Apply (one time)

SignPath Foundation's main conditions (check the current list on their site):

- The repository is **public** on GitHub and under an OSI-approved license (Kenma is MIT ✔).
- Releases are built from source by CI, not uploaded from a developer machine (✔ GitHub Actions).
- The project is actively maintained and has a released version.
- The README has a code signing policy section (template below).

Apply at **https://signpath.org** and link `https://github.com/syedghayyurabbas/kenma`.

## 2. Configure SignPath (after approval)

In the SignPath web app:

1. Note your **organization ID**.
2. Create a project with the slug `kenma`, connected to the GitHub repository as a trusted build system.
3. Create a signing policy with the slug `release-signing`.
4. Use this **artifact configuration**. GitHub artifacts arrive as a zip, and every `Kenma.exe` inside is Authenticode-signed:

   ```xml
   <?xml version="1.0" encoding="utf-8"?>
   <artifact-configuration xmlns="http://signpath.io/artifact-configuration/v1">
     <zip-file>
       <pe-file-set>
         <include path="**/Kenma.exe" />
         <for-each>
           <authenticode-sign />
         </for-each>
       </pe-file-set>
     </zip-file>
   </artifact-configuration>
   ```

5. Create an API token for CI (a CI user with submitter rights on the project).

## 3. Configure GitHub

In **Settings → Secrets and variables → Actions** of the repository:

| Kind | Name | Value |
|---|---|---|
| Variable | `SIGNPATH_ORGANIZATION_ID` | your organization ID |
| Variable | `SIGNPATH_PROJECT_SLUG` | `kenma` |
| Variable | `SIGNPATH_SIGNING_POLICY_SLUG` | `release-signing` |
| Secret | `SIGNPATH_API_TOKEN` | the CI API token |

The next `v*` tag produces signed builds. Depending on the policy, a maintainer may need to approve each
signing request in SignPath before the workflow continues.

## 4. README section (required by SignPath Foundation, add once approved)

```markdown
## Code signing policy

Free code signing provided by [SignPath.io](https://signpath.io), certificate by [SignPath Foundation](https://signpath.org).

- Committers and reviewers: [syedghayyurabbas](https://github.com/syedghayyurabbas)
- Approvers: [syedghayyurabbas](https://github.com/syedghayyurabbas)

Privacy: this program does not transfer any information to other networked systems unless specifically
requested by the user. Prompts are sent only to the AI engine the user selects. See [Privacy](#privacy).
```

Note that SmartScreen reputation also builds up over downloads, so early signed releases may still show a
warning for a short while.

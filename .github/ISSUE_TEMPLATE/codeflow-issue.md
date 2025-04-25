name: Codeflow issue
description: Report an issue with VMR codeflow PRs
body:
  - type: input
    id: prUrl
    attributes:
      label: Pull request
      description: Enter link to a pull request in which the issue is observed
    validations:
      required: true
  - type: dropdown
    id: type
    attributes:
      label: Type of problem
      description: What problem are you experiencing?
      options:
      - Missing/incomplete documentation
      - Incorrect contents in the PR
      - Codeflow PRs not getting created
      - Unexpected behavior in the PR (e.g. conflicts, EOLs, mangled files, ..)
      - Problem with version files (Versions.Details.xml, Versions.props, global.json, ..)
      - General help required
      default: 0
    validations:
      required: true
  - type: textarea
    attributes:
      label: Description of the issue
      description: Provide any details that could help with the investigation (subscription information, link to associated commits..).
    validations:
      required: true
  - type: markdown
    attributes:
      value: |
        ## Additional information
        If asking for general assistance, please make sure you've checked the [codeflow documentation](https://github.com/dotnet/arcade/blob/main/Documentation/UnifiedBuild/VMR-Full-Code-Flow.md)

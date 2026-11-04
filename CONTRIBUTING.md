# Contributing to Sharper

Thank you for helping improve Sharper. Its value comes from early feedback: warm LSP services can return focused rule findings faster and with less work than repeatedly running a full build or test suite. The earlier an agent learns about a violation, the sooner it can correct course, before more changes depend on the mistake. Contributions should strengthen that short feedback loop or make the tool easier to use, understand or maintain.

## Open an issue first

Before starting a contribution, [open an issue](https://github.com/adrian13579/sharper/issues/new) or join an existing discussion. Describe the problem, who it affects and the outcome you want. For bugs, include reproduction steps, expected and actual behavior, relevant versions and a minimal example. Remove secrets and private source from reports.

Discuss the scope and approach with maintainers before investing in implementation. An issue helps establish whether the change fits the project and avoids overlapping work; opening one does not guarantee acceptance.

## Explain the design and value

Every proposed contribution should include a design/contribution document linked from the issue and pull request. Use the [design template](.github/CONTRIBUTION_DESIGN_TEMPLATE.md). Keep the document proportionate: a small correction may need only a few paragraphs, while a feature or behavior change needs enough detail to review the approach and tradeoffs.

Explain:

- **Problem and value:** what is missing or broken, who benefits, and what the change offers.
- **Proposed behavior:** how users or contributors will experience the change, with before/after examples where useful.
- **Design and scope:** how the solution works, affected interfaces, boundaries and what is outside scope.
- **Alternatives and tradeoffs:** why this approach fits, including compatibility, performance or maintenance costs.

The document can be Markdown attached to the issue/PR or a committed file in the contribution branch. Make it accessible to reviewers and keep it aligned with the final implementation. Do not substitute a file inventory or generated changelog for an explanation of the change.

## Implement a focused change

Link your work to the agreed issue. Keep the contribution focused on one coherent purpose, preserve unrelated changes, and update the website documentation when public behavior changes. Keep source operations read-only and preserve honest scope, completion and freshness reporting. External LSP services own language semantics and rules; Sharper connects their findings to agents.

Use [AGENTS.md](AGENTS.md) for repository layout and architectural boundaries. The public documentation is maintained in `website/content/docs/` and published on the [documentation website](https://adrian13579.github.io/sharper/docs/overview/).

## Open a pull request

Link the issue and design/contribution document. Describe the problem, resulting behavior, user benefit. Call out compatibility changes, limitations and follow-up work. Use focused Conventional Commits, and update your proposal if the implementation changes during review.

Be prepared to explain design decisions and revise the change based on feedback. Maintainers may request a smaller scope or decline changes that do not fit the project.

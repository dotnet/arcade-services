import { Component, Input } from '@angular/core';
import { Build, BuildIncoherence } from 'src/maestro-client/models';

@Component({
  selector: 'popIncoherencies',
  templateUrl: './popIncoherencies.component.html'
})

export class PopIncoherenciesComponent {
  @Input() data: Build | undefined;

  private compareIncoherenciesByName(a: BuildIncoherence, b: BuildIncoherence) {
    if (a.name === undefined) return -1;
    if (b.name === undefined) return 1;

    return (a.name > b.name) ? 1 : a.name === b.name ? 0 : -1
  }

  groupedIncoherencies() {
    if (this.data === undefined) return;
    if (this.data.incoherencies === undefined) return this.data;

    const sortedIncoherencies = this.data.incoherencies.sort((a, b) => this.compareIncoherenciesByName(a, b));

    const groups = sortedIncoherencies.reduce(function (r, a) {
      if (a.name === undefined) return r;
      r[a.name] = r[a.name] || [];
      r[a.name].push(a);
      return r;
    }, Object.create(null));

    const summaries = [];

    for (const groupName in groups) {
      summaries.push(new IncoherencySummary(groupName, groups[groupName]));
    }

    return summaries;
  }
}

export class IncoherencySummary {
  dependencyName: string | undefined;
  incoherencies: BuildIncoherence[] | undefined;

  constructor(name: string, incoherencies: BuildIncoherence[]) {
    this.dependencyName = name;
    this.incoherencies = incoherencies;
  }
}

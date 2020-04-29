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

  sortedIncoherencies() {
    if (this.data === undefined) return;
    if (this.data.incoherencies === undefined) return this.data;

    return this.data.incoherencies.sort((a, b) => this.compareIncoherenciesByName(a, b));
  }
}


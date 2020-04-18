import { Component, Input } from '@angular/core';
import { Build } from 'src/maestro-client/models';

@Component({
  selector: 'popIncoherencies',
  templateUrl: './popIncoherencies.component.html'
})

export class PopIncoherenciesComponent {
  @Input() data: Build;

  sortedIncoherencies() {
    if (this.data == undefined) return this.data;
    if (this.data.incoherencies == undefined) return this.data;

    return this.data.incoherencies.sort((a, b) => a.name > b.name ? 1 : a.name === b.name ? 0 : -1);
  }
}


import { Component, Input } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';

import { environment } from "src/environments/environment";

@Component({
  selector: 'generic-error',
  template: `
        <div class="alert alert-danger" role="alert" aria-hidden="true">
            <span class="glyphicon glyphicon-exclamation-sign"></span>
            <span class="sr-only">Error:</span>
            Unable to load content
            <p *ngIf="!prod">{{errorText}}</p>
        </div>
    `,
  styles: []
})
export class GenericErrorComponent {
  @Input() public error: any;

  public prod = environment.production;

  public get errorText(): string {
    if (this.error instanceof HttpErrorResponse) {
      return `Http Error: ${this.error.statusText}`;
    }
    return this.error && this.error.toString && this.error.toString() || "Unknown Error";
  }
}

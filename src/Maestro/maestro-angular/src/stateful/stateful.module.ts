import { NgModule } from "@angular/core";
import { StatefulDirective } from "./stateful.directive";
import { GenericErrorComponent } from "./generic-error/generic-error.component";
import { ProgressRingComponent } from "./progress-ring/progress-ring.component";
import { CommonModule } from '@angular/common';


@NgModule({
    imports: [
        CommonModule,
    ],
    declarations: [
        StatefulDirective,
        GenericErrorComponent,
        ProgressRingComponent,
    ],
    entryComponents: [
        GenericErrorComponent,
        ProgressRingComponent,
    ],
    exports: [
        StatefulDirective,
        ProgressRingComponent,
    ],
})
export class StatefulModule {}

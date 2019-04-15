import { Component, OnInit, Input, OnChanges, TemplateRef } from '@angular/core';
import { TreeNode } from '../definitions';
import {
  faAngleDown,
  faAngleRight,
} from "@fortawesome/free-solid-svg-icons";
import { Observable, of } from 'rxjs';
import { tap } from 'rxjs/operators';
import { statefulSwitchMap, statefulPipe, StatefulResult } from 'src/stateful';
import { trigger, transition, style, animate } from '@angular/animations';

@Component({
  selector: 'tree-node',
  templateUrl: './tree-node.component.html',
  styleUrls: ['./tree-node.component.scss'],
  animations: [
    trigger("expandCollapse", [
      transition(":enter", [
        style({ height: 0 }),
        animate("400ms ease-out", style({ height: "*" })),
      ]),
      transition(":leave", [
        style({ height: "*" }),
        animate("400ms ease-in", style({ height: 0 })),
      ]),
    ]),
  ]
})
export class TreeNodeComponent implements OnChanges {
  faAngleDown = faAngleDown;
  faAngleRight = faAngleRight;

  @Input() public nodeTemplate?: TemplateRef<any>;
  @Input() public data?: TreeNode;

  public hasChildren: boolean = false;
  public open: boolean = false;
  public children$?: Observable<StatefulResult<TreeNode[]>>;

  constructor() { }

  ngOnChanges() {
    if (this.data) {
      if ('children' in this.data && this.data.children) {
        this.open = !!this.data.startOpen;
        this.hasChildren = this.data.children && !!this.data.children.length;
        this.children$ = of(this.data.children);
        return;
      } else if ('children$' in this.data) {
        this.open = !!this.data.startOpen;
        this.hasChildren = true;
        this.children$ = of(this.data.children$).pipe(
          statefulSwitchMap(fn => fn()),
          statefulPipe(
            tap(c => {
              this.hasChildren = c && !!c.length;
            }),
          ),
        );
        return;
      }
    }
    this.hasChildren = false;
    this.open = false;
    this.children$ = undefined;
  }

}

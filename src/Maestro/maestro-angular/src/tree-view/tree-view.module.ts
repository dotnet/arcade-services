import { NgModule } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FontAwesomeModule } from "@fortawesome/angular-fontawesome";

import { TreeViewComponent } from './tree-view/tree-view.component';
import { TreeNodeComponent } from './tree-node/tree-node.component';
import { TreeNodeListComponent } from './tree-node-list/tree-node-list.component';
import { StatefulModule } from 'src/stateful';

@NgModule({
  declarations: [TreeViewComponent, TreeNodeComponent, TreeNodeListComponent],
  imports: [
    CommonModule,
    FontAwesomeModule,
    StatefulModule,
  ],
  exports: [TreeViewComponent, TreeNodeListComponent]
})
export class TreeViewModule { }

﻿using System;
using System.Collections.Generic;
using System.Web.Mvc;
using Commands;
using Website.CommandService;
using ReadModel;
using System.Linq;

namespace Website.Controllers
{
    public class NoteController : Controller
    {
        public ActionResult Index()
        {
            IEnumerable<NoteItem> items;

            using (var context = new ReadModelContainer())
            {
                var query = from item in context.NoteItemSet
                            orderby item.CreationDate
                            select item;

                items = query.ToArray();
            }

            return View(items);
        }

        public ActionResult Edit(Guid id)
        {
            NoteItem item;

            using (var context = new ReadModelContainer())
            {
                item = context.NoteItemSet.Single(note => note.Id == id) ;
            }

            var command = new ChangeNoteText();
            command.NoteId = id;
            command.NewText = item.Text;

            return View(command);
        }

        [HttpPost]
        public ActionResult Edit(ChangeNoteText command)
        {
            var service = new MyNotesCommandServiceClient();
            service.ChangeNoteText(command);

            // Return user back to the index that
            // displays all the notes.));
            return RedirectToAction("Index");
        }

        public ActionResult Add()
        {
            return View();
        }

        [HttpPost]
        public ActionResult Add(CreateNewNote command)
        {
            var service = new MyNotesCommandServiceClient();
            service.CreateNewNote(command);

            // Return user back to the index that
            // displays all the notes.));
            return RedirectToAction("Index");
        }
    }
}
